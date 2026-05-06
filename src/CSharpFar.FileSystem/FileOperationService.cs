using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class FileOperationService : IFileOperationService
{
    // ── Create folder ─────────────────────────────────────────────────────────

    public void CreateDirectory(string path)
    {
        if (Directory.Exists(path))
            throw new IOException($"Folder '{Path.GetFileName(path)}' already exists.");
        Directory.CreateDirectory(path);
    }

    // ── Copy ──────────────────────────────────────────────────────────────────

    public Task CopyAsync(
        IReadOnlyList<string> sources,
        string destination,
        Action<string>? onProgress = null,
        Func<string, ConflictChoice>? onConflict = null,
        CancellationToken cancellationToken = default)
    {
        foreach (string src in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(src))
                CopyFile(src, destination, onProgress, onConflict, cancellationToken);
            else if (Directory.Exists(src))
                CopyDirectory(src, destination, onProgress, onConflict, cancellationToken);
        }
        return Task.CompletedTask;
    }

    private static void CopyFile(
        string srcFile,
        string destDir,
        Action<string>? onProgress,
        Func<string, ConflictChoice>? onConflict,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string fileName = Path.GetFileName(srcFile);
        string destFile = Path.Combine(destDir, fileName);

        onProgress?.Invoke(fileName);

        if (PathsEqual(srcFile, destFile))
            throw new IOException("Source and destination file are the same.");

        if (File.Exists(destFile))
        {
            var choice = onConflict?.Invoke(destFile) ?? ConflictChoice.Overwrite;
            switch (choice)
            {
                case ConflictChoice.Skip:   return;
                case ConflictChoice.Cancel: throw new OperationCanceledException("Copy cancelled by user.");
                // Overwrite: fall through
            }
        }

        File.Copy(srcFile, destFile, overwrite: true);
    }

    private static void CopyDirectory(
        string srcDir,
        string destParent,
        Action<string>? onProgress,
        Func<string, ConflictChoice>? onConflict,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string dirName = Path.GetFileName(srcDir);
        string destDir = Path.Combine(destParent, dirName);
        if (PathsEqual(srcDir, destDir))
            throw new IOException("Source and destination directory are the same.");
        if (IsPathInside(destDir, srcDir))
            throw new IOException("Cannot copy a directory into itself.");

        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.GetFiles(srcDir))
        {
            ct.ThrowIfCancellationRequested();
            CopyFile(file, destDir, onProgress, onConflict, ct);
        }

        foreach (string subDir in Directory.GetDirectories(srcDir))
        {
            ct.ThrowIfCancellationRequested();
            CopyDirectory(subDir, destDir, onProgress, onConflict, ct);
        }
    }

    // ── Move / Rename ─────────────────────────────────────────────────────────

    public Task MoveAsync(
        IReadOnlyList<string> sources,
        string destination,
        Func<string, ConflictChoice>? onConflict = null,
        CancellationToken cancellationToken = default)
    {
        if (sources.Count == 1 && !IsPath(destination))
        {
            // Rename: destination is a plain name (no path separators)
            string src     = sources[0];
            string newPath = Path.Combine(Path.GetDirectoryName(src)!, destination);
            MoveItem(src, newPath, onConflict);
        }
        else
        {
            // Move each source into the destination directory
            foreach (string src in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destPath = Path.Combine(destination, Path.GetFileName(src));
                MoveItem(src, destPath, onConflict);
            }
        }
        return Task.CompletedTask;
    }

    private static bool IsPath(string s) =>
        s.Contains(Path.DirectorySeparatorChar) ||
        s.Contains(Path.AltDirectorySeparatorChar) ||
        Path.IsPathRooted(s);

    private static void MoveItem(string src, string dest, Func<string, ConflictChoice>? onConflict)
    {
        if (PathsEqual(src, dest))
            return;

        if (Directory.Exists(src) && IsPathInside(dest, src))
            throw new IOException("Cannot move a directory into itself.");

        if (File.Exists(dest) || Directory.Exists(dest))
        {
            var choice = onConflict?.Invoke(dest) ?? ConflictChoice.Overwrite;
            switch (choice)
            {
                case ConflictChoice.Skip:   return;
                case ConflictChoice.Cancel: throw new OperationCanceledException("Move cancelled by user.");
                // Overwrite: remove destination then move
            }
            if (File.Exists(dest))           File.Delete(dest);
            else if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        }

        if (File.Exists(src))           File.Move(src, dest);
        else if (Directory.Exists(src)) Directory.Move(src, dest);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsPathInside(string path, string possibleParent)
    {
        string child = NormalizePath(path);
        string parent = NormalizePath(possibleParent);
        if (!parent.EndsWith(Path.DirectorySeparatorChar))
            parent += Path.DirectorySeparatorChar;

        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(path))           File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            // Non-existent path: silently skip
        }
        return Task.CompletedTask;
    }
}
