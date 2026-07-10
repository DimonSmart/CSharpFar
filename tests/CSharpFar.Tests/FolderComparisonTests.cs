using CSharpFar.Core.Comparison;

namespace CSharpFar.Tests;

public sealed class FolderComparisonTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CSharpFarCompareTests", Guid.NewGuid().ToString("N"));
    private readonly string _left;
    private readonly string _right;

    public FolderComparisonTests()
    {
        _left = Path.Combine(_root, "left");
        _right = Path.Combine(_root, "right");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FolderStructure_FastComparison_EqualFilesWithSameRelativePath()
    {
        Write(_left, "src/App.cs", "abc", T(0));
        Write(_right, "src/App.cs", "abc", T(0));

        var row = FolderRows()[0];

        Assert.Equal(CompareStatus.Equal, row.Status);
        Assert.Equal("src/App.cs", row.Key);
    }

    [Fact]
    public void FolderStructure_FastComparison_DifferentBySize()
    {
        Write(_left, "file.txt", "abc", T(0));
        Write(_right, "file.txt", "abcd", T(0));

        var row = FolderRows()[0];

        Assert.Equal(CompareStatus.Different, row.Status);
    }

    [Fact]
    public void FolderStructure_ContentComparison_DifferentBytesWithSameSize()
    {
        Write(_left, "file.txt", "abc", T(0));
        Write(_right, "file.txt", "abd", T(0));

        var row = FolderRows(new ComparisonOptions { Method = CompareMethod.Content })[0];

        Assert.Equal(CompareStatus.Different, row.Status);
    }

    [Fact]
    public void FolderStructure_ReportsLeftOnlyAndRightOnly()
    {
        Write(_left, "left.txt", "left", T(0));
        Write(_right, "right.txt", "right", T(0));

        var statuses = FolderRows().ToDictionary(row => row.Key, row => row.Status);

        Assert.Equal(CompareStatus.LeftOnly, statuses["left.txt"]);
        Assert.Equal(CompareStatus.RightOnly, statuses["right.txt"]);
    }

    [Fact]
    public void Scanner_RecursiveScanning_IncludesSubfolderFiles()
    {
        Write(_left, "a/b.txt", "x", T(0));
        Write(_right, "a/b.txt", "x", T(0));

        Assert.Contains(FolderRows(), row => row.Key == "a/b.txt" && row.Status == CompareStatus.Equal);
    }

    [Fact]
    public void Scanner_NonRecursiveScanning_IgnoresSubfolderFiles()
    {
        Write(_left, "a/b.txt", "x", T(0));
        Write(_right, "a/b.txt", "x", T(0));

        var rows = FolderRows(new ComparisonOptions { IncludeSubfolders = false });

        Assert.Empty(rows);
    }

    [Fact]
    public void Scanner_MaximumDepth_IgnoresDeeperFiles()
    {
        Write(_left, "a/one.txt", "1", T(0));
        Write(_right, "a/one.txt", "1", T(0));
        Write(_left, "a/b/two.txt", "2", T(0));
        Write(_right, "a/b/two.txt", "2", T(0));

        var rows = FolderRows(new ComparisonOptions { MaxDepth = 1 });

        Assert.Contains(rows, row => row.Key == "a/one.txt");
        Assert.DoesNotContain(rows, row => row.Key == "a/b/two.txt");
    }

    [Fact]
    public void Scanner_IncludeMask_IncludesOnlyMatchingFiles()
    {
        Write(_left, "a.cs", "1", T(0));
        Write(_right, "a.cs", "1", T(0));
        Write(_left, "a.md", "1", T(0));
        Write(_right, "a.md", "1", T(0));

        var rows = FolderRows(new ComparisonOptions { IncludeMasks = "*.cs" });

        Assert.Single(rows);
        Assert.Equal("a.cs", rows[0].Key);
    }

    [Fact]
    public void Scanner_ExcludeMask_SkipsFilesAndFolders()
    {
        Write(_left, "keep.txt", "1", T(0));
        Write(_right, "keep.txt", "1", T(0));
        Write(_left, "bin/drop.txt", "1", T(0));
        Write(_right, "bin/drop.txt", "1", T(0));
        Write(_left, "skip.tmp", "1", T(0));
        Write(_right, "skip.tmp", "1", T(0));

        var rows = FolderRows(new ComparisonOptions { ExcludeMasks = "bin;*.tmp" });

        Assert.Single(rows);
        Assert.Equal("keep.txt", rows[0].Key);
    }

    [Fact]
    public void FolderStructure_TimestampTolerance_ControlsFastEquality()
    {
        Write(_left, "file.txt", "abc", T(0));
        Write(_right, "file.txt", "abc", T(1));

        var exact = FolderRows(new ComparisonOptions { TimestampTolerance = TimestampTolerance.Exact })[0];
        var tolerant = FolderRows(new ComparisonOptions { TimestampTolerance = TimestampTolerance.TwoSeconds })[0];

        Assert.Equal(CompareStatus.Different, exact.Status);
        Assert.Equal(CompareStatus.Equal, tolerant.Status);
    }

    [Fact]
    public void FileSet_FileName_MatchesSameNameInDifferentFolders()
    {
        Write(_left, "2023/photo.jpg", "abc", T(0));
        Write(_right, "backup/photo.jpg", "abc", T(0));

        var row = FileSetRows()[0];

        Assert.Equal(CompareStatus.Equal, row.Status);
        Assert.Equal("photo.jpg", row.Key);
    }

    [Fact]
    public void FileSet_FileName_ReportsLeftOnly()
    {
        Write(_left, "old.zip", "x", T(0));

        var row = FileSetRows()[0];

        Assert.Equal(CompareStatus.LeftOnly, row.Status);
    }

    [Fact]
    public void FileSet_FileName_ReportsRightOnly()
    {
        Write(_right, "new.zip", "x", T(0));

        var row = FileSetRows()[0];

        Assert.Equal(CompareStatus.RightOnly, row.Status);
    }

    [Fact]
    public void FileSet_FileName_DuplicateNameIsAmbiguousAndNotPaired()
    {
        Write(_left, "a/photo.jpg", "1", T(0));
        Write(_left, "b/photo.jpg", "1", T(0));
        Write(_right, "photo.jpg", "1", T(0));

        var row = FileSetRows()[0];

        Assert.Equal(CompareStatus.Ambiguous, row.Status);
        Assert.Equal(2, row.LeftEntries.Count);
        Assert.Single(row.RightEntries);
    }

    [Fact]
    public void FileSet_FileNameAndSize_SeparatesSameNameWithDifferentSizes()
    {
        Write(_left, "a/photo.jpg", "1", T(0));
        Write(_right, "b/photo.jpg", "22", T(0));

        var rows = FileSetRows(new ComparisonOptions { FileSetMatchMode = FileSetMatchMode.FileNameAndSize });

        Assert.Contains(rows, row => row.Key == "photo.jpg|1" && row.Status == CompareStatus.LeftOnly);
        Assert.Contains(rows, row => row.Key == "photo.jpg|2" && row.Status == CompareStatus.RightOnly);
    }

    [Fact]
    public void FileSet_FileNameAndContentHash_MatchesIdenticalContentWithSameName()
    {
        Write(_left, "a/logo.png", "same", T(0));
        Write(_right, "b/logo.png", "same", T(99));

        var row = FileSetRows(new ComparisonOptions { FileSetMatchMode = FileSetMatchMode.FileNameAndContentHash })[0];

        Assert.Equal(CompareStatus.Equal, row.Status);
    }

    [Fact]
    public void FileSet_FileNameAndContentHash_SeparatesSameNameWithDifferentContent()
    {
        Write(_left, "a/readme.md", "left", T(0));
        Write(_right, "b/readme.md", "right", T(0));

        var rows = FileSetRows(new ComparisonOptions { FileSetMatchMode = FileSetMatchMode.FileNameAndContentHash });

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Status == CompareStatus.LeftOnly);
        Assert.Contains(rows, row => row.Status == CompareStatus.RightOnly);
    }

    [Fact]
    public void FolderStructure_FileReadError_ReturnsErrorAndContinues()
    {
        Write(_left, "bad.txt", "abc", T(0));
        Write(_right, "bad.txt", "abc", T(0));
        Write(_left, "good.txt", "ok", T(0));
        Write(_right, "good.txt", "ok", T(0));
        var fileSystem = new ThrowingOpenFileSystem(Path.Combine(_left, "bad.txt"));
        var engine = new FolderStructureCompareEngine(fileSystem: fileSystem);

        var result = engine.Compare(
            new FolderScanRequest { RootPath = _left },
            new FolderScanRequest { RootPath = _right },
            new ComparisonOptions { Method = CompareMethod.Content });

        Assert.Contains(result.Rows, row => row.Key == "bad.txt" && row.Status == CompareStatus.Error);
        Assert.Contains(result.Rows, row => row.Key == "good.txt" && row.Status == CompareStatus.Equal);
    }

    [Fact]
    public void FolderStructure_CaseInsensitiveMode_MatchesNamesIgnoringCase()
    {
        Write(_left, "File.txt", "abc", T(0));
        Write(_right, "file.txt", "abc", T(0));

        var rows = FolderRows(new ComparisonOptions { NameComparison = NameComparisonMode.CaseInsensitive });

        Assert.Single(rows);
        Assert.Equal(CompareStatus.Equal, rows[0].Status);
    }

    [Fact]
    public void FolderStructure_CaseSensitiveMode_DoesNotMatchNamesWithDifferentCase()
    {
        Write(_left, "File.txt", "abc", T(0));
        Write(_right, "file.txt", "abc", T(0));

        var rows = FolderRows(new ComparisonOptions { NameComparison = NameComparisonMode.CaseSensitive });

        Assert.Contains(rows, row => row.Key == "File.txt" && row.Status == CompareStatus.LeftOnly);
        Assert.Contains(rows, row => row.Key == "file.txt" && row.Status == CompareStatus.RightOnly);
    }

    private IReadOnlyList<CompareResultRow> FolderRows(ComparisonOptions? options = null) =>
        new FolderStructureCompareEngine().Compare(
            new FolderScanRequest { RootPath = _left },
            new FolderScanRequest { RootPath = _right },
            options ?? new ComparisonOptions()).Rows;

    private IReadOnlyList<CompareResultRow> FileSetRows(ComparisonOptions? options = null) =>
        new FileSetCompareEngine().Compare(
            new FolderScanRequest { RootPath = _left },
            new FolderScanRequest { RootPath = _right },
            options ?? new ComparisonOptions()).Rows;

    private void Write(string root, string relativePath, string content, DateTime lastWriteTimeUtc)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
    }

    private static DateTime T(int seconds) =>
        new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);

    private sealed class ThrowingOpenFileSystem : LocalComparisonFileSystem
    {
        private readonly string _throwPath;

        public ThrowingOpenFileSystem(string throwPath)
        {
            _throwPath = throwPath;
        }

        public override Stream OpenRead(string path)
        {
            if (string.Equals(path, _throwPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Simulated read failure.");

            return base.OpenRead(path);
        }
    }
}
