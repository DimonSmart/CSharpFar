using CSharpFar.App.Viewer;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class DirectoryTreeSizeScannerTests
{
    [Fact]
    public void Scan_SumsNestedRegularFiles()
    {
        var source = new FakeSource();
        source.Set("/root",
            Dir("/root/sub"),
            File("/root/a.bin", 10));
        source.Set("/root/sub",
            Dir("/root/sub/deep"),
            File("/root/sub/b.bin", 20));
        source.Set("/root/sub/deep", File("/root/sub/deep/c.bin", 30));

        var result = new DirectoryTreeSizeScanner(0).Scan(source, "/root", null, default);

        Assert.Equal(60, result.Size);
        Assert.Equal(DirectoryTreeSizeCompletionStatus.Completed, result.Status);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Scan_ReportsProgressWithAccumulatedSize()
    {
        var source = new FakeSource();
        source.Set("/root", File("/root/a", 10), File("/root/b", 20));
        var progress = new List<DirectoryTreeSizeProgress>();

        var result = new DirectoryTreeSizeScanner(0).Scan(source, "/root", progress.Add, default);

        Assert.Equal(30, result.Size);
        Assert.Contains(progress, item => item.Size == 10);
        Assert.Contains(progress, item => item.Size == 30);
    }

    [Fact]
    public void Scan_SkipsDirectoryLinksFileLinksAndMountPoints()
    {
        var source = new FakeSource();
        source.Set("/root",
            Dir("/root/ordinary"),
            Dir("/root/link-dir", FileAttributes.ReparsePoint),
            File("/root/link-file", 500, FileAttributes.ReparsePoint),
            Mount("/root/mount"),
            File("/root/plain", 7));
        source.Set("/root/ordinary", File("/root/ordinary/count", 11));
        source.Set("/root/link-dir", File("/root/link-dir/must-not-count", 1000));
        source.Set("/root/mount", File("/root/mount/must-not-count", 2000));

        var result = new DirectoryTreeSizeScanner(0).Scan(source, "/root", null, default);

        Assert.Equal(18, result.Size);
        Assert.Equal(DirectoryTreeSizeCompletionStatus.Completed, result.Status);
        Assert.DoesNotContain("/root/link-dir", source.EnumeratedPaths.Skip(1));
        Assert.DoesNotContain("/root/mount", source.EnumeratedPaths.Skip(1));
    }

    [Fact]
    public void Scan_ChildEnumerationFailureReturnsPartialResultWithErrors()
    {
        var source = new FakeSource();
        source.Set("/root", File("/root/a", 10), Dir("/root/denied"), File("/root/b", 20));
        source.Fail("/root/denied", new UnauthorizedAccessException("denied"));

        var result = new DirectoryTreeSizeScanner(0).Scan(source, "/root", null, default);

        Assert.Equal(30, result.Size);
        Assert.Equal(DirectoryTreeSizeCompletionStatus.CompletedWithErrors, result.Status);
        Assert.Single(result.Errors);
        Assert.Contains("denied", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scan_RootEnumerationFailureIsFailedNotEmptyDirectory()
    {
        var source = new FakeSource();
        source.Fail("/root", new DirectoryNotFoundException("gone"));

        var result = new DirectoryTreeSizeScanner(0).Scan(source, "/root", null, default);

        Assert.Equal(0, result.Size);
        Assert.Equal(DirectoryTreeSizeCompletionStatus.Failed, result.Status);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Scan_UnknownFileSizeProducesPartialResultWithErrors()
    {
        var source = new FakeSource();
        source.Set("/root", File("/root/known", 12), File("/root/unknown", null));

        var result = new DirectoryTreeSizeScanner(0).Scan(source, "/root", null, default);

        Assert.Equal(12, result.Size);
        Assert.Equal(DirectoryTreeSizeCompletionStatus.CompletedWithErrors, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_CancellationIsPropagated()
    {
        var source = new FakeSource();
        source.Set("/root", File("/root/a", 1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new DirectoryTreeSizeScanner(0).Scan(source, "/root", null, cts.Token));
    }

    [Fact]
    public void Scan_DuplicateDirectoryPathDoesNotLoop()
    {
        var source = new FakeSource();
        source.Set("/root", Dir("/root/a"), Dir("/root/a"));
        source.Set("/root/a", File("/root/a/file", 9));

        var result = new DirectoryTreeSizeScanner(0).Scan(source, "/root", null, default);

        Assert.Equal(9, result.Size);
        Assert.Equal(DirectoryTreeSizeCompletionStatus.CompletedWithErrors, result.Status);
        Assert.Equal(1, source.EnumeratedPaths.Count(path => path == "/root/a"));
    }

    [Fact]
    public void TraversalPolicy_RejectsParentLinksAndMounts()
    {
        Assert.False(DirectoryTraversalPolicy.IsEligibleDirectory(new FilePanelItem
        {
            Name = "..", FullPath = "/", SourceId = FakeSource.Id,
            IsDirectory = true, IsParentDirectory = true,
        }));
        Assert.False(DirectoryTraversalPolicy.IsEligibleDirectory(Dir("/link", FileAttributes.ReparsePoint)));
        Assert.False(DirectoryTraversalPolicy.IsEligibleDirectory(Mount("/mount")));
        Assert.True(DirectoryTraversalPolicy.IsEligibleDirectory(Dir("/ordinary")));
    }

    private static FilePanelItem Dir(string path, FileAttributes extra = 0) => new()
    {
        Name = Name(path),
        FullPath = path,
        SourceId = FakeSource.Id,
        IsDirectory = true,
        Size = null,
        Attributes = FileAttributes.Directory | extra,
    };

    private static FilePanelItem Mount(string path) => new()
    {
        Name = Name(path),
        FullPath = path,
        SourceId = FakeSource.Id,
        IsDirectory = true,
        Size = null,
        Attributes = FileAttributes.Directory,
        IsVolumeMountPoint = true,
    };

    private static FilePanelItem File(string path, long? size, FileAttributes extra = 0) => new()
    {
        Name = Name(path),
        FullPath = path,
        SourceId = FakeSource.Id,
        IsDirectory = false,
        Size = size,
        Attributes = FileAttributes.Normal | extra,
    };

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    private sealed class FakeSource : IFilePanelSource
    {
        public static readonly PanelSourceId Id = new("directory-size-test");
        private readonly Dictionary<string, IReadOnlyList<FilePanelItem>> _directories = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Exception> _failures = new(StringComparer.Ordinal);

        public List<string> EnumeratedPaths { get; } = [];
        public PanelSourceId SourceId => Id;
        public string DisplayName => "directory-size-test";
        public PanelProviderCapabilities Capabilities => PanelProviderCapabilities.Enumerate;
        public IReadOnlyCollection<char> PathSeparators => ['/'];

        public void Set(string path, params FilePanelItem[] items) => _directories[NormalizePath(path)] = items;
        public void Fail(string path, Exception exception) => _failures[NormalizePath(path)] = exception;

        public string NormalizePath(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return "/";
            string normalized = sourcePath.Replace('\\', '/');
            if (!normalized.StartsWith('/')) normalized = "/" + normalized;
            while (normalized.Length > 1 && normalized.EndsWith('/')) normalized = normalized[..^1];
            return normalized;
        }

        public bool IsRootPath(string sourcePath) => NormalizePath(sourcePath) == "/";
        public string? GetParentPath(string sourcePath)
        {
            string path = NormalizePath(sourcePath);
            if (path == "/") return null;
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? "/" : path[..slash];
        }

        public IReadOnlyList<FilePanelItem> EnumerateDirectory(string sourcePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = NormalizePath(sourcePath);
            EnumeratedPaths.Add(path);
            if (_failures.TryGetValue(path, out Exception? failure)) throw failure;
            return _directories.TryGetValue(path, out IReadOnlyList<FilePanelItem>? items) ? items : [];
        }

        public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default) => null;
        public Task<Stream> OpenReadAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(string sourcePath, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RenameAsync(string sourcePath, string newSourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
