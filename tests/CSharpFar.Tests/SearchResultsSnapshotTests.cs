using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class SearchResultsSnapshotTests : IDisposable
{
    private readonly string _root;

    public SearchResultsSnapshotTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSearchSnapshot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ExternalCommand_DoesNotRefreshSearchResultsSnapshot()
    {
        string foundPath = Path.Combine(_root, "found.txt");
        var searchService = new CountingSearchService();
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var app = CreateApp(CreateFileSystem(FileItem(foundPath)), driver, searchService);
        var state = app.Session.Panels.Left;
        OpenSnapshot(app, state, foundPath);

        app.ExecuteInCurrentConsole(_root, "dir", () => { });

        Assert.Equal([foundPath], state.Items.Select(item => item.FullPath));
        Assert.Equal(0, searchService.Calls);
    }

    [Fact]
    public void GenericRefresh_SearchResultsPreservesSnapshotWhenFilesystemChanges()
    {
        string oldPath = Path.Combine(_root, "old.txt");
        string newPath = Path.Combine(_root, "new.txt");
        var searchService = new CountingSearchService();
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var fs = CreateFileSystem(FileItem(oldPath));
        var app = CreateApp(fs, driver, searchService);
        var state = app.Session.Panels.Left;
        OpenSnapshot(app, state, oldPath);

        fs.AddDirectory(_root, FileItem(newPath));

        app.RefreshPanels();

        Assert.Equal([oldPath], state.Items.Select(item => item.FullPath));
        Assert.DoesNotContain(state.Items, item => item.FullPath == newPath);
        Assert.Equal(0, searchService.Calls);
    }

    [Fact]
    public void GenericRefresh_LocalFilesystemPanelStillReloadsDirectory()
    {
        string oldPath = Path.Combine(_root, "old.txt");
        string newPath = Path.Combine(_root, "new.txt");
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var fs = CreateFileSystem(FileItem(oldPath));
        var app = CreateApp(fs, driver, new CountingSearchService());
        var state = app.Session.Panels.Left;

        fs.AddDirectory(_root, FileItem(newPath));

        app.RefreshPanels();

        Assert.Contains(state.Items, item => item.FullPath == newPath);
        Assert.DoesNotContain(state.Items, item => item.FullPath == oldPath);
    }

    [Fact]
    public void OpeningResultsFromNewExplicitSearch_ReplacesPreviousSnapshot()
    {
        string firstPath = Path.Combine(_root, "first.txt");
        string secondPath = Path.Combine(_root, "second.txt");
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var app = CreateApp(CreateFileSystem(), driver, new CountingSearchService());
        var state = app.Session.Panels.Left;

        OpenSnapshot(app, state, firstPath);
        OpenSnapshot(app, state, secondPath);

        Assert.Equal([secondPath], state.Items.Select(item => item.FullPath));
        Assert.Equal("*.txt", state.SearchRequest?.FileMaskExpression);
    }

    private Application CreateApp(
        FakeFileSystemService fs,
        FakeConsoleDriver driver,
        ISearchService searchService)
    {
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            searchService: searchService);
    }

    private FakeFileSystemService CreateFileSystem(params FilePanelItem[] items)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root, items);
        return fs;
    }

    private void OpenSnapshot(Application app, FilePanelState state, string fullPath)
    {
        app.OpenSearchResultsPanel(
            state,
            new SearchRequest
            {
                RootPath = _root,
                FileMaskExpression = "*.txt",
                Scope = SearchScope.CurrentDirectoryRecursive,
                MaxDegreeOfParallelism = 1,
            },
            [
                new SearchResultItem
                {
                    FullPath = fullPath,
                    Name = Path.GetFileName(fullPath),
                    Kind = SearchResultItemKind.File,
                    Size = 1,
                    LastWriteTime = new DateTime(2026, 1, 1),
                    Attributes = FileAttributes.Archive,
                },
            ],
            cancelled: false);
    }

    private static FilePanelItem FileItem(string fullPath) =>
        new()
        {
            Name = Path.GetFileName(fullPath),
            FullPath = fullPath,
            IsDirectory = false,
            Size = 1,
            LastWriteTime = new DateTime(2026, 1, 1),
            Attributes = FileAttributes.Archive,
        };

    private sealed class CountingSearchService : ISearchService
    {
        public int Calls { get; private set; }

        public async IAsyncEnumerable<SearchResultItem> SearchAsync(
            SearchRequest request,
            IProgress<SearchProgress>? progress,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.CompletedTask;
            yield break;
        }
    }
}
