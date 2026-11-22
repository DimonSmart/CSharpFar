namespace CSharpFar.FileSystem;

// A replacement is made from a complete same-directory staging file. Existing
// bytes are never removed to make room for a copy still in progress.
internal static class SafeLocalFilePublisher
{
    internal static string CreateStagingPath(string target) =>
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

    // A non-null return value is a retained backup that could not be cleaned up.
    internal static string? Publish(string staging, string destination)
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
        try { File.Replace(staging, destination, backup); }
        catch (Exception error)
        {
            if (PathExists(backup))
            {
                try { File.Move(backup, destination, overwrite: true); }
                catch (Exception rollback)
                {
                    throw new IOException($"Publication and rollback failed for '{destination}'. Original backup: '{backup}'.",
                        new AggregateException(error, rollback));
                }
            }
            throw new IOException($"Publication failed for '{destination}'; previous destination preserved.", error);
        }

        try { File.Delete(backup); return null; }
        catch (IOException) { return backup; }
        catch (UnauthorizedAccessException) { return backup; }
    }
}
