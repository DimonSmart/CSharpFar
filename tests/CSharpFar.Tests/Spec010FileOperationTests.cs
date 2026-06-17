using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class Spec010FileOperationTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;
    private readonly string _destination;

    public Spec010FileOperationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec010_{Guid.NewGuid():N}");
        _source = Path.Combine(_root, "source");
        _destination = Path.Combine(_root, "destination");
        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_destination);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ExecuteAsync_CopyReportsByteProgressAndResultCounts()
    {
        string file = Write(_source, "data.txt", "hello");
        var progress = new List<FileOperationProgress>();

        FileOperationResult result = await ExecuteAsync(
            FileOperationKind.Copy,
            [file],
            _destination,
            new FileOperationOptions(),
            progress: new Progress<FileOperationProgress>(progress.Add));

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(5, result.TotalBytes);
        Assert.Contains(progress, p => p.TotalBytesTotal == 5);
        Assert.Contains(progress, p => p.Phase == FileOperationPhase.Scanning);
        Assert.Contains(progress, p => p.Phase == FileOperationPhase.Copying && p.ItemsTotal == 1 && p.TotalBytesTotal == 5);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_destination, "data.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_CopyMaskUsesFarMaskMatcher()
    {
        string nested = Path.Combine(_source, "nested");
        Directory.CreateDirectory(nested);
        Write(_source, "keep.txt", "a");
        Write(_source, "skip.tmp", "b");
        Write(nested, "nested.txt", "c");

        await ExecuteAsync(
            FileOperationKind.Copy,
            [_source],
            _destination,
            new FileOperationOptions { FileMask = "*.txt|skip*" });

        string copiedRoot = Path.Combine(_destination, "source");
        Assert.True(File.Exists(Path.Combine(copiedRoot, "keep.txt")));
        Assert.True(File.Exists(Path.Combine(copiedRoot, "nested", "nested.txt")));
        Assert.False(File.Exists(Path.Combine(copiedRoot, "skip.tmp")));
    }

    [Fact]
    public async Task ExecuteAsync_OnlyNewerSkipsOlderSource()
    {
        string source = Write(_source, "same.txt", "old");
        string destination = Write(_destination, "same.txt", "new");
        File.SetLastWriteTime(source, new DateTime(2024, 1, 1));
        File.SetLastWriteTime(destination, new DateTime(2025, 1, 1));

        FileOperationResult result = await ExecuteAsync(
            FileOperationKind.Copy,
            [source],
            _destination,
            new FileOperationOptions { OnlyNewer = true });

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("new", File.ReadAllText(destination));
    }

    [Fact]
    public async Task ExecuteAsync_PreservesTimestampsWhenEnabled()
    {
        string source = Write(_source, "time.txt", "content");
        var expected = new DateTime(2023, 5, 4, 3, 2, 1);
        File.SetCreationTime(source, expected);
        File.SetLastWriteTime(source, expected);
        File.SetLastAccessTime(source, expected);

        await ExecuteAsync(
            FileOperationKind.Copy,
            [source],
            _destination,
            new FileOperationOptions { PreserveTimestamps = true });

        string copied = Path.Combine(_destination, "time.txt");
        Assert.Equal(expected, File.GetLastWriteTime(copied));
    }

    [Fact]
    public async Task ExecuteAsync_RestoresDirectoryTimestampsAfterChildrenAreCopied()
    {
        string childDirectory = Path.Combine(_source, "child");
        Directory.CreateDirectory(childDirectory);
        Write(childDirectory, "file.txt", "content");
        var expected = new DateTime(2023, 5, 4, 3, 2, 1);
        Directory.SetLastWriteTime(childDirectory, expected);

        FileOperationResult result = await ExecuteAsync(
            FileOperationKind.Copy,
            [_source],
            _destination,
            new FileOperationOptions { PreserveTimestamps = true });

        string copied = Path.Combine(_destination, "source", "child");
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(Path.Combine(copied, "file.txt")));
        Assert.Equal(expected, Directory.GetLastWriteTime(copied));
    }

    [Fact]
    public async Task ExecuteAsync_RenameAllGeneratesPredictableNames()
    {
        string source = Write(_source, "dup.txt", "new");
        Write(_destination, "dup.txt", "old");

        await ExecuteAsync(
            FileOperationKind.Copy,
            [source],
            _destination,
            new FileOperationOptions(),
            new FixedConflictResolver(ConflictDecisionMode.RenameAll));

        Assert.Equal("old", File.ReadAllText(Path.Combine(_destination, "dup.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_destination, "dup (2).txt")));
    }

    [Fact]
    public async Task ExecuteAsync_AppendAddsSourceBytesToExistingFile()
    {
        string source = Write(_source, "dup.txt", "new");
        Write(_destination, "dup.txt", "old");

        await ExecuteAsync(
            FileOperationKind.Copy,
            [source],
            _destination,
            new FileOperationOptions(),
            new FixedConflictResolver(ConflictDecisionMode.Append));

        Assert.Equal("oldnew", File.ReadAllText(Path.Combine(_destination, "dup.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_AppendAllAppliesToLaterConflictsInSameOperation()
    {
        string first = Write(_source, "first.txt", "new1");
        string second = Write(_source, "second.txt", "new2");
        Write(_destination, "first.txt", "old1");
        Write(_destination, "second.txt", "old2");

        await ExecuteAsync(
            FileOperationKind.Copy,
            [first, second],
            _destination,
            new FileOperationOptions(),
            new FixedConflictResolver(ConflictDecisionMode.AppendAll));

        Assert.Equal("old1new1", File.ReadAllText(Path.Combine(_destination, "first.txt")));
        Assert.Equal("old2new2", File.ReadAllText(Path.Combine(_destination, "second.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_CreateDirectoryCreatesNestedPath()
    {
        string path = Path.Combine(_source, "a", "b", "c");

        await ExecuteAsync(
            FileOperationKind.CreateDirectory,
            [],
            path,
            new FileOperationOptions());

        Assert.True(Directory.Exists(path));
    }

    private static async Task<FileOperationResult> ExecuteAsync(
        FileOperationKind kind,
        IReadOnlyList<string> sources,
        string? destination,
        FileOperationOptions options,
        IFileOperationConflictResolver? resolver = null,
        IProgress<FileOperationProgress>? progress = null)
    {
        return await new FileOperationService().ExecuteAsync(
            new FileOperationRequest
            {
                Kind = kind,
                Sources = sources,
                Destination = destination,
                Options = options,
            },
            progress,
            resolver ?? new FixedConflictResolver(ConflictDecisionMode.Overwrite));
    }

    private static string Write(string directory, string name, string content)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class FixedConflictResolver : IFileOperationConflictResolver
    {
        private readonly ConflictDecisionMode _mode;

        public FixedConflictResolver(ConflictDecisionMode mode)
        {
            _mode = mode;
        }

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(_mode);
    }
}
