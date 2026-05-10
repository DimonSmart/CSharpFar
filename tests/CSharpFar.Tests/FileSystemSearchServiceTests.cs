using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class FileSystemSearchServiceTests : IDisposable
{
    private readonly string _root;

    public FileSystemSearchServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSearchTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        string sub = Directory.CreateDirectory(Path.Combine(_root, "sub")).FullName;
        string deep = Directory.CreateDirectory(Path.Combine(sub, "deep")).FullName;
        File.WriteAllText(Path.Combine(_root, "a.cs"), "Customer");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "beta");
        File.WriteAllText(Path.Combine(_root, "word.txt"), "Customer account");
        File.WriteAllText(Path.Combine(sub, "c.cs"), "customer");
        File.WriteAllText(Path.Combine(sub, "id.txt"), "CustomerId");
        File.WriteAllText(Path.Combine(deep, "e.cs"), "deep");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task SearchAsync_FindsFilesByNameRecursively()
    {
        var results = await SearchAsync(new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "*.cs",
            Scope = SearchScope.CurrentDirectoryRecursive,
            MaxDegreeOfParallelism = 2,
        });

        Assert.Equal(["a.cs", "c.cs", "e.cs"], results.Select(r => r.Name).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_CurrentDirectoryOnlyDoesNotSearchChildren()
    {
        var results = await SearchAsync(new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "*.cs",
            Scope = SearchScope.CurrentDirectoryOnly,
            MaxDegreeOfParallelism = 2,
        });

        Assert.Equal(["a.cs"], results.Select(r => r.Name));
    }

    [Fact]
    public async Task SearchAsync_UsesFarMaskExpression()
    {
        var results = await SearchAsync(new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "*.cs|a*",
            Scope = SearchScope.CurrentDirectoryRecursive,
            MaxDegreeOfParallelism = 2,
        });

        Assert.Equal(["c.cs", "e.cs"], results.Select(r => r.Name).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_CaseSensitiveContentSearchRespectsCase()
    {
        var results = await SearchAsync(new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "*.cs",
            ContainingText = "Customer",
            CaseSensitive = true,
            Scope = SearchScope.CurrentDirectoryRecursive,
            MaxDegreeOfParallelism = 2,
        });

        var result = Assert.Single(results);
        Assert.Equal("a.cs", result.Name);
        Assert.Equal(1, result.LineNumber);
        Assert.Equal("Customer", result.MatchedTextPreview);
    }

    [Fact]
    public async Task SearchAsync_WholeWordsDoesNotMatchInsideIdentifier()
    {
        var results = await SearchAsync(new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "*.txt",
            ContainingText = "Customer",
            WholeWords = true,
            Scope = SearchScope.CurrentDirectoryRecursive,
            MaxDegreeOfParallelism = 2,
        });

        var result = Assert.Single(results);
        Assert.Equal("word.txt", result.Name);
    }

    [Fact]
    public async Task SearchAsync_NotContainingReturnsCheckedFilesWithoutText()
    {
        var results = await SearchAsync(new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "*.txt",
            ContainingText = "Customer",
            NotContaining = true,
            Scope = SearchScope.CurrentDirectoryRecursive,
            MaxDegreeOfParallelism = 2,
        });

        Assert.Contains(results, r => r.Name == "b.txt");
        Assert.DoesNotContain(results, r => r.Name == "id.txt");
        Assert.DoesNotContain(results, r => r.Name == "word.txt");
    }

    [Fact]
    public async Task SearchAsync_IncludesDirectoriesOnlyWhenRequested()
    {
        var withoutDirectories = await SearchAsync(new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "sub",
            Scope = SearchScope.CurrentDirectoryOnly,
            MaxDegreeOfParallelism = 2,
        });

        var withDirectories = await SearchAsync(new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "sub",
            IncludeDirectoriesInResults = true,
            Scope = SearchScope.CurrentDirectoryOnly,
            MaxDegreeOfParallelism = 2,
        });

        Assert.Empty(withoutDirectories);
        var directory = Assert.Single(withDirectories);
        Assert.Equal(SearchResultItemKind.Directory, directory.Kind);
        Assert.Equal("sub", directory.Name);
    }

    [Fact]
    public async Task SearchAsync_SkipsBinaryAndLargeFilesWithDiagnostics()
    {
        File.WriteAllBytes(Path.Combine(_root, "binary.bin"), [0, 1, 2, 3]);
        File.WriteAllText(Path.Combine(_root, "large.txt"), "0123456789");
        var progress = new RecordingSearchProgress();

        var results = await SearchAsync(new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "*",
            ContainingText = "needle",
            Scope = SearchScope.CurrentDirectoryOnly,
            MaxDegreeOfParallelism = 2,
            MaxContentSearchFileSizeBytes = 4,
        }, progress);

        Assert.Empty(results);
        Assert.Contains(progress.Items, p => p.ErrorCount > 0);
    }

    [Fact]
    public async Task SearchAsync_ReportsProgress()
    {
        var progress = new RecordingSearchProgress();

        await SearchAsync(new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "*.cs",
            Scope = SearchScope.CurrentDirectoryRecursive,
            MaxDegreeOfParallelism = 2,
        }, progress);

        Assert.Contains(progress.Items, p => p.ScannedDirectories > 0);
        Assert.Contains(progress.Items, p => p.ScannedFiles > 0);
        Assert.Contains(progress.Items, p => p.MatchedItems > 0);
    }

    [Fact]
    public async Task SearchAsync_CancellationThrowsAfterKeepingStreamedResultsAvailable()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await SearchAsync(new SearchRequest
            {
                RootPath = _root,
                FileMaskExpression = "*.cs",
                Scope = SearchScope.CurrentDirectoryRecursive,
                MaxDegreeOfParallelism = 2,
            }, cancellationToken: cts.Token);
        });
    }

    private static async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        SearchRequest request,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SearchResultItem>();
        await foreach (var item in new FileSystemSearchService()
            .SearchAsync(request, progress, cancellationToken))
        {
            results.Add(item);
        }

        return results;
    }

    private sealed class RecordingSearchProgress : IProgress<SearchProgress>
    {
        public List<SearchProgress> Items { get; } = [];

        public void Report(SearchProgress value) => Items.Add(value);
    }
}
