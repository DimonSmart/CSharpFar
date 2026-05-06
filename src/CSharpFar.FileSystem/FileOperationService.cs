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

    // ── Move / Delete (future stages) ─────────────────────────────────────────

    public Task MoveAsync(IReadOnlyList<string> sources, string destination, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Coming in Stage 8.");

    public Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Coming in Stage 9.");
}
