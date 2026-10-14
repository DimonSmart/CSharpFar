using CSharpFar.App.Commands;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class SearchResultsDeleteSupportTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CSharpFarSearchDelete_").FullName;

    [Fact]
    public void CollapseNestedSources_KeepsOnlyTopLevelSelectedRoots()
    {
        string parent = Path.Combine(_root, "obj");
        string child = Path.Combine(parent, "cache", "file.bin");
        string sibling = Path.Combine(_root, "other", "obj");

        var result = SearchResultsDeleteSupport.CollapseNestedSources([child, parent, sibling]);

        Assert.Equal([parent, sibling], result);
    }


    [Fact]
    public void CollapseNestedSources_IdenticalPathsAreIncludedOnce()
    {
        string path = Path.Combine(_root, "file.txt");

        var result = SearchResultsDeleteSupport.CollapseNestedSources([path, path]);

        Assert.Equal([path], result);
    }

    [Fact]
    public void CollapseNestedSources_CaseVariantsFollowLocalFileSystemPolicy()
    {
        string upper = Path.Combine(_root, "Report.txt");
        string lower = Path.Combine(_root, "report.txt");

        var result = SearchResultsDeleteSupport.CollapseNestedSources([upper, lower]);

        if (OperatingSystem.IsWindows())
            Assert.Equal([upper], result);
        else
            Assert.Equal([upper, lower], result);
    }

    [Fact]
    public void CollapseNestedSources_DirectoryCaseVariantsFollowLocalFileSystemPolicy()
    {
        string directory = Path.Combine(_root, "Data");
        string caseDifferentChild = Path.Combine(_root, "data", "file.txt");

        var result = SearchResultsDeleteSupport.CollapseNestedSources([directory, caseDifferentChild]);

        if (OperatingSystem.IsWindows())
            Assert.Equal([directory], result);
        else
            Assert.Equal([directory, caseDifferentChild], result);
    }

    [Fact]
    public void Reconcile_CaseVariantsFollowLocalFileSystemPolicy()
    {
        string upper = Path.Combine(_root, "Report.txt");
        string lower = Path.Combine(_root, "report.txt");
        var state = SearchState(upper, lower);

        SearchResultsDeleteSupport.Reconcile(
            state,
            [upper],
            new FileOperationResult { Kind = FileOperationKind.Delete, Errors = [] });

        if (OperatingSystem.IsWindows())
            Assert.Empty(state.Items);
        else
            Assert.Equal([lower], state.Items.Select(item => item.FullPath));
    }

    [Fact]
    public void DeleteAndReconcile_OnCaseSensitiveFileSystem_PreservesCaseDifferentFile()
    {
        if (OperatingSystem.IsWindows())
            return;

        string upper = Path.Combine(_root, "Report.txt");
        string lower = Path.Combine(_root, "report.txt");
        File.WriteAllText(upper, "upper");
        File.WriteAllText(lower, "lower");

        // Some Unix installations use a case-insensitive volume.
        if (File.ReadAllText(upper) != "upper" || File.ReadAllText(lower) != "lower")
            return;

        var state = SearchState(upper, lower);
        Assert.Equal([upper, lower], SearchResultsDeleteSupport.CollapseNestedSources([upper, lower]));

        File.Delete(upper);
        SearchResultsDeleteSupport.Reconcile(
            state,
            [upper],
            new FileOperationResult { Kind = FileOperationKind.Delete, Errors = [] });

        Assert.Equal([lower], state.Items.Select(item => item.FullPath));
        Assert.True(File.Exists(lower));
        Assert.Equal("lower", File.ReadAllText(lower));
    }

    [Fact]
    public void Reconcile_CleanDeleteRemovesRootAndDescendantResults()
    {
        string deletedRoot = Path.Combine(_root, "obj");
        string descendant = Path.Combine(deletedRoot, "cache.bin");
        string untouched = Path.Combine(_root, "keep.txt");
        var state = SearchState(deletedRoot, descendant, untouched);

        SearchResultsDeleteSupport.Reconcile(
            state,
            [deletedRoot],
            new FileOperationResult { Kind = FileOperationKind.Delete, Errors = [] });

        Assert.Equal([untouched], state.Items.Select(item => item.FullPath));
    }

    [Fact]
    public void Reconcile_PartialFailureKeepsAffectedItemsThatStillExist()
    {
        string existing = Path.Combine(_root, "existing.txt");
        File.WriteAllText(existing, "keep");
        string missing = Path.Combine(_root, "missing.txt");
        var state = SearchState(existing, missing);

        SearchResultsDeleteSupport.Reconcile(
            state,
            [existing, missing],
            new FileOperationResult
            {
                Kind = FileOperationKind.Delete,
                Cancelled = true,
                Errors = [],
            });

        Assert.Equal([existing], state.Items.Select(item => item.FullPath));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static FilePanelState SearchState(params string[] paths)
    {
        var state = new FilePanelState
        {
            SearchRequest = new SearchRequest
            {
                RootPath = Path.GetDirectoryName(paths[0]) ?? string.Empty,
                FileMaskExpression = "*",
                Scope = SearchScope.CurrentDirectoryRecursive,
                MaxDegreeOfParallelism = 1,
            },
        };
        state.Items.AddRange(paths.Select(path => new FilePanelItem
        {
            Name = Path.GetFileName(path),
            FullPath = path,
            IsDirectory = false,
        }));
        return state;
    }
}
