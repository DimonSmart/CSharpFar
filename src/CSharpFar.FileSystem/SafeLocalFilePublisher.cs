namespace CSharpFar.FileSystem;

// A replacement is made from a complete same-directory staging file. Existing
// bytes are never removed to make room for a copy still in progress.
public static class SafeLocalFilePublisher
{
    public static string CreateStagingPath(string target) =>
        Path.Combine(Path.GetDirectoryName(target)!,
            $".{Path.GetFileName(target)}.csharpfar-{Guid.NewGuid():N}.tmp");

    internal static bool PathExists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static void DeleteStagingPath(string path)
    {
        if (!PathExists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
            Directory.Delete(path, recursive: false);
        else File.Delete(path);
    }

    internal static void MoveStagingPath(string source, string target)
    {
        if ((File.GetAttributes(source) & FileAttributes.Directory) != 0)
            Directory.Move(source, target);
        else File.Move(source, target);
    }

    public readonly record struct DestinationSnapshot(long Length, DateTime LastWriteTimeUtc,
        DateTime CreationTimeUtc, FileAttributes Attributes);

    public static DestinationSnapshot? CaptureDestination(string path)
    {
        if (!PathExists(path)) return null;
        var info = new FileInfo(path);
        var attributes = info.Attributes;
        return new DestinationSnapshot(
            (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ? 0 : info.Length,
            info.LastWriteTimeUtc, info.CreationTimeUtc, attributes);
    }

    public static string? PublishChecked(string staging, string destination, DestinationSnapshot? expected) =>
        PublishChecked(staging, destination, expected, null, null);

    internal static string? PublishChecked(string staging, string destination, DestinationSnapshot? expected,
        Func<string, string, string?>? replace = null, Action<string, string>? create = null)
    {
        if (CaptureDestination(destination) != expected)
            throw new IOException($"Destination changed before publication: '{destination}'.");
        if (expected is null)
        {
            (create ?? MoveStagingPath)(staging, destination);
            return null;
        }
        return (replace ?? Publish)(staging, destination);
    }

    // A non-null return value is a retained backup that could not be cleaned up.
    internal sealed record PublicationOperations
    {
        internal Action<string, string, string> Replace { get; init; } = (source, target, backup) => File.Replace(source, target, backup);
        internal Action<string, string> Move { get; init; } = File.Move;
        internal Action<string> Delete { get; init; } = File.Delete;
    }

    internal static string? Publish(string staging, string destination) => Publish(staging, destination, new PublicationOperations());

    internal static string? Publish(string staging, string destination, PublicationOperations operations)
    {
        if (Directory.Exists(destination) ||
            (PathExists(destination) && (File.GetAttributes(destination) & FileAttributes.Directory) != 0))
            throw new IOException($"Cannot replace directory '{destination}' with a file.");

        if (!PathExists(destination))
        {
            MoveStagingPath(staging, destination);
            return null;
        }

        if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Cannot safely replace a symbolic link: '{destination}'.");
        if ((File.GetAttributes(staging) & FileAttributes.Directory) != 0)
            throw new IOException($"Cannot safely replace a file with a directory link: '{destination}'.");

        string backup = CreateStagingPath(destination) + ".backup";
        // File.Replace rejects ReadOnly destinations on Windows. A backed-up
        // rename is the available fallback: not an atomic transaction, so the
        // original stays at the known backup path until commit succeeds.
        if (OperatingSystem.IsWindows() &&
            (File.GetAttributes(destination) & FileAttributes.ReadOnly) != 0)
            return PublishReadOnlyWindowsFile(staging, destination, backup, operations);

        try { operations.Replace(staging, destination, backup); }
        catch (Exception error)
        {
            if (PathExists(backup))
            {
                try { operations.Move(backup, destination); }
                catch (Exception rollback)
                {
                    throw new IOException($"Publication and rollback failed for '{destination}'. Original backup: '{backup}'.",
                        new AggregateException(error, rollback));
                }
            }
            throw new IOException($"Publication failed for '{destination}'; previous destination preserved.", error);
        }

        try { operations.Delete(backup); return null; }
        catch (IOException) { return backup; }
        catch (UnauthorizedAccessException) { return backup; }
    }
    private static string? PublishReadOnlyWindowsFile(string staging, string destination, string backup, PublicationOperations operations)
    {
        operations.Move(destination, backup);
        try
        {
            operations.Move(staging, destination);
        }
        catch (Exception error)
        {
            try { operations.Move(backup, destination); }
            catch (Exception restoreError)
            {
                throw new IOException(
                    $"Publishing '{staging}' to '{destination}' failed and rollback failed. " +
                    $"Original file is retained at '{backup}'.",
                    new AggregateException(error, restoreError));
            }
            throw new IOException(
                $"Publishing '{staging}' to '{destination}' failed; original destination restored.",
                error);
        }

        // The successful new file is now published. Removing the old backup
        // may require clearing ReadOnly; never touch the published destination.
        try
        {
            File.SetAttributes(backup, File.GetAttributes(backup) & ~FileAttributes.ReadOnly);
            operations.Delete(backup);
            return null;
        }
        catch (IOException) { return backup; }
        catch (UnauthorizedAccessException) { return backup; }
    }
}
