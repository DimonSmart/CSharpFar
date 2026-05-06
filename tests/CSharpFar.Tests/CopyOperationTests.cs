using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 7: F5 copy — FileOperationService.CopyAsync.
/// </summary>
public class CopyOperationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _src;
    private readonly string _dst;

    public CopyOperationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"CSharpFarCopyTest_{Guid.NewGuid():N}");
        _src      = Path.Combine(_tempRoot, "src");
        _dst      = Path.Combine(_tempRoot, "dst");
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_dst);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private FileOperationService Svc() => new();

    // ── Single file ───────────────────────────────────────────────────────────

    [Fact]
    public void CopyAsync_CopiesSingleFile()
    {
        string srcFile = Write(_src, "hello.txt", "hello");

        Svc().CopyAsync([srcFile], _dst).GetAwaiter().GetResult();

        Assert.True(File.Exists(Path.Combine(_dst, "hello.txt")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_dst, "hello.txt")));
    }

    [Fact]
    public void CopyAsync_CopiesMultipleFiles()
    {
        string f1 = Write(_src, "a.txt", "A");
        string f2 = Write(_src, "b.txt", "B");

        Svc().CopyAsync([f1, f2], _dst).GetAwaiter().GetResult();

        Assert.True(File.Exists(Path.Combine(_dst, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_dst, "b.txt")));
    }

    [Fact]
    public void CopyAsync_SourceFileIsNotDeleted()
    {
        string srcFile = Write(_src, "keep.txt", "data");

        Svc().CopyAsync([srcFile], _dst).GetAwaiter().GetResult();

        Assert.True(File.Exists(srcFile));
    }

    // ── Directory ─────────────────────────────────────────────────────────────

    [Fact]
    public void CopyAsync_CopiesDirectoryRecursively()
    {
        string sub = Path.Combine(_src, "sub");
        Directory.CreateDirectory(sub);
        Write(_src, "root.txt", "r");
        Write(sub,  "nested.txt", "n");

        Svc().CopyAsync([_src], _dst).GetAwaiter().GetResult();

        string copiedSrc = Path.Combine(_dst, Path.GetFileName(_src));
        Assert.True(File.Exists(Path.Combine(copiedSrc, "root.txt")));
        Assert.True(File.Exists(Path.Combine(copiedSrc, "sub", "nested.txt")));
    }

    // ── Progress callback ─────────────────────────────────────────────────────

    [Fact]
    public void CopyAsync_CallsOnProgressForEachFile()
    {
        Write(_src, "a.txt", "");
        Write(_src, "b.txt", "");

        var reported = new List<string>();
        Svc().CopyAsync(
            [Path.Combine(_src, "a.txt"), Path.Combine(_src, "b.txt")],
            _dst,
            onProgress: name => reported.Add(name))
            .GetAwaiter().GetResult();

        Assert.Contains("a.txt", reported);
        Assert.Contains("b.txt", reported);
    }

    // ── Conflict handling ─────────────────────────────────────────────────────

    [Fact]
    public void CopyAsync_CallsOnConflictWhenDestExists()
    {
        string srcFile = Write(_src, "dup.txt", "new");
        Write(_dst, "dup.txt", "old");

        bool conflictCalled = false;
        Svc().CopyAsync(
            [srcFile], _dst,
            onConflict: _ => { conflictCalled = true; return ConflictChoice.Skip; })
            .GetAwaiter().GetResult();

        Assert.True(conflictCalled);
    }

    [Fact]
    public void CopyAsync_OverwritesWhenChoiceIsOverwrite()
    {
        string srcFile = Write(_src, "dup.txt", "new content");
        Write(_dst, "dup.txt", "old content");

        Svc().CopyAsync(
            [srcFile], _dst,
            onConflict: _ => ConflictChoice.Overwrite)
            .GetAwaiter().GetResult();

        Assert.Equal("new content", File.ReadAllText(Path.Combine(_dst, "dup.txt")));
    }

    [Fact]
    public void CopyAsync_SkipsFileWhenChoiceIsSkip()
    {
        string srcFile = Write(_src, "dup.txt", "new content");
        Write(_dst, "dup.txt", "old content");

        Svc().CopyAsync(
            [srcFile], _dst,
            onConflict: _ => ConflictChoice.Skip)
            .GetAwaiter().GetResult();

        Assert.Equal("old content", File.ReadAllText(Path.Combine(_dst, "dup.txt")));
    }

    [Fact]
    public void CopyAsync_ThrowsOperationCancelledWhenChoiceIsCancel()
    {
        string srcFile = Write(_src, "dup.txt", "new");
        Write(_dst, "dup.txt", "old");

        Assert.Throws<OperationCanceledException>(() =>
            Svc().CopyAsync(
                [srcFile], _dst,
                onConflict: _ => ConflictChoice.Cancel)
                .GetAwaiter().GetResult());
    }

    [Fact]
    public void CopyAsync_StopsAtCancelEvenWithMoreSources()
    {
        string f1 = Write(_src, "dup.txt",  "new");
        string f2 = Write(_src, "other.txt", "other");
        Write(_dst, "dup.txt", "old");

        Assert.Throws<OperationCanceledException>(() =>
            Svc().CopyAsync(
                [f1, f2], _dst,
                onConflict: _ => ConflictChoice.Cancel)
                .GetAwaiter().GetResult());

        Assert.False(File.Exists(Path.Combine(_dst, "other.txt")));
    }

    // ── CancellationToken ─────────────────────────────────────────────────────

    [Fact]
    public void CopyAsync_RespectsExternalCancellation()
    {
        Write(_src, "a.txt", "");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            Svc().CopyAsync(
                [Path.Combine(_src, "a.txt")], _dst,
                cancellationToken: cts.Token)
                .GetAwaiter().GetResult());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string Write(string dir, string name, string content)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
