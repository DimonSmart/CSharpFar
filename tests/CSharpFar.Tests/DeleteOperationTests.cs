using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 9: F8 delete — FileOperationService.DeleteAsync.
/// </summary>
public class DeleteOperationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _src;

    public DeleteOperationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"CSharpFarDeleteTest_{Guid.NewGuid():N}");
        _src      = Path.Combine(_tempRoot, "src");
        Directory.CreateDirectory(_src);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private FileOperationService Svc() => new();

    // ── Single file ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_DeletesSingleFile()
    {
        string f = Write(_src, "file.txt", "data");

        await Svc().DeleteAsync([f]);

        Assert.False(File.Exists(f));
    }

    // ── Directory ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_DeletesDirectoryRecursively()
    {
        string sub = Path.Combine(_src, "subdir");
        Directory.CreateDirectory(sub);
        Write(sub, "inner.txt", "x");

        await Svc().DeleteAsync([_src]);

        Assert.False(Directory.Exists(_src));
    }

    // ── Multiple items ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_DeletesMultipleFiles()
    {
        string f1 = Write(_src, "a.txt", "A");
        string f2 = Write(_src, "b.txt", "B");

        await Svc().DeleteAsync([f1, f2]);

        Assert.False(File.Exists(f1));
        Assert.False(File.Exists(f2));
    }

    // ── Non-existent path silently skipped ────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SkipsNonExistentPath()
    {
        string ghost = Path.Combine(_src, "ghost.txt");

        // Should not throw
        await Svc().DeleteAsync([ghost]);
    }

    // ── CancellationToken ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RespectsExternalCancellation()
    {
        string f1 = Write(_src, "a.txt", "");
        string f2 = Write(_src, "b.txt", "");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Svc().DeleteAsync([f1, f2], cts.Token));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string Write(string dir, string name, string content)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
