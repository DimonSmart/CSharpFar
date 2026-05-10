using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec012SearchResultsPanelTests : IDisposable
{
    private readonly string _root;

    public Spec012SearchResultsPanelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec012_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Run_DeleteInSearchResultsPanelIsDisabled()
    {
        string filePath = Path.Combine(_root, "found.txt");
        var fileOps = new RecordingFileOperationService();
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        driver.EnqueueKey(Key(ConsoleKey.F8));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(CreateFileSystem(), driver, fileOps);
        SetSearchResultsPanel(GetLeftPanel(app), filePath);

        app.Run();

        Assert.Empty(fileOps.Requests);
    }

    [Fact]
    public void Run_CopyIntoSearchResultsPanelIsBlocked()
    {
        string localFile = Path.Combine(_root, "local.txt");
        var fileOps = new RecordingFileOperationService();
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F5));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var fs = CreateFileSystem(new FilePanelItem
        {
            Name = "local.txt",
            FullPath = localFile,
            IsDirectory = false,
        });
        var app = CreateApp(fs, driver, fileOps);
        SetSearchResultsPanel(GetRightPanel(app), Path.Combine(_root, "found.txt"));

        app.Run();

        Assert.Empty(fileOps.Requests);
    }

    [Fact]
    public void Run_CopyFromSearchResultsPanelUsesRealPaths()
    {
        string foundFile = Path.Combine(_root, "found.txt");
        var fileOps = new RecordingFileOperationService();
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        driver.EnqueueKey(Key(ConsoleKey.F5));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(CreateFileSystem(), driver, fileOps);
        SetSearchResultsPanel(GetLeftPanel(app), foundFile);

        app.Run();

        var request = Assert.Single(fileOps.Requests);
        Assert.Equal(FileOperationKind.Copy, request.Kind);
        Assert.Equal([foundFile], request.Sources);
        Assert.Equal(_root, request.Destination);
    }

    [Fact]
    public void PanelStatusRenderer_ShowsFullPathForSearchResults()
    {
        string foundFile = Path.Combine(_root, "found.txt");
        var state = new FilePanelState
        {
            CurrentDirectory = _root,
            ShowCurrentItemFullPath = true,
        };
        state.Items.Add(new FilePanelItem
        {
            Name = "found.txt",
            FullPath = foundFile,
            IsDirectory = false,
            Size = 1,
        });

        string row = CSharpFar.App.Rendering.PanelStatusRenderer.FormatCurrentItem(state, 200);

        Assert.Contains(foundFile, row);
    }

    [Fact]
    public void Run_EnterOnSearchResultFileLoadsParentDirectoryAndSelectsFile()
    {
        string subDirectory = Path.Combine(_root, "sub");
        string foundFile = Path.Combine(subDirectory, "found.txt");
        var fileOps = new RecordingFileOperationService();
        var launcher = new RecordingFileLauncher();
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var fs = CreateFileSystem();
        fs.AddDirectory(
            subDirectory,
            new FilePanelItem
            {
                Name = "found.txt",
                FullPath = foundFile,
                IsDirectory = false,
                Size = 1,
                LastWriteTime = new DateTime(2026, 1, 1),
            });

        var app = CreateApp(fs, driver, fileOps, fileLauncher: launcher);
        SetSearchResultsPanel(GetLeftPanel(app), foundFile);

        app.Run();

        var left = GetLeftPanel(app);
        Assert.Equal(subDirectory, left.CurrentDirectory);
        Assert.Equal(PanelProviderCapabilities.LocalFileSystem, left.ProviderCapabilities);
        Assert.Null(left.SearchRequest);
        Assert.False(left.ShowCurrentItemFullPath);
        Assert.Equal("found.txt", left.Items[left.CursorIndex].Name);
        Assert.Empty(launcher.OpenedFiles);
    }

    [Fact]
    public void Run_EnterOnSearchResultDirectoryLoadsDirectoryAsLocalPanel()
    {
        string foundDirectory = Path.Combine(_root, "found");
        string childFile = Path.Combine(foundDirectory, "child.txt");
        var fileOps = new RecordingFileOperationService();
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var fs = CreateFileSystem();
        fs.AddDirectory(
            foundDirectory,
            new FilePanelItem
            {
                Name = "child.txt",
                FullPath = childFile,
                IsDirectory = false,
                Size = 1,
                LastWriteTime = new DateTime(2026, 1, 1),
            });

        var app = CreateApp(fs, driver, fileOps);
        SetSearchResultsPanel(GetLeftPanel(app), foundDirectory, isDirectory: true);

        app.Run();

        var left = GetLeftPanel(app);
        Assert.Equal(foundDirectory, left.CurrentDirectory);
        Assert.Equal(PanelProviderCapabilities.LocalFileSystem, left.ProviderCapabilities);
        Assert.Null(left.SearchRequest);
        Assert.False(left.ShowCurrentItemFullPath);
        Assert.Contains(left.Items, item => item.Name == "child.txt");
    }

    [Fact]
    public void Run_GoToDuringSearchLoadsRealParentDirectoryAndSelectsFoundFile()
    {
        string subDirectory = Path.Combine(_root, "sub");
        string foundFile = Path.Combine(subDirectory, "found.txt");
        var fileOps = new RecordingFileOperationService();
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.F7, alt: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.NoName));
        driver.EnqueueKey(Key(ConsoleKey.NoName));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var fs = new FakeFileSystemService();
        fs.AddDirectory(
            _root,
            new FilePanelItem
            {
                Name = "sub",
                FullPath = subDirectory,
                IsDirectory = true,
            });
        fs.AddDirectory(
            subDirectory,
            new FilePanelItem
            {
                Name = "found.txt",
                FullPath = foundFile,
                IsDirectory = false,
                Size = 1,
                LastWriteTime = new DateTime(2026, 1, 1),
            });

        var app = CreateApp(fs, driver, fileOps, new BlockingSearchService(SearchResult(foundFile)));

        app.Run();

        var left = GetLeftPanel(app);
        Assert.Equal(subDirectory, left.CurrentDirectory);
        Assert.Equal(PanelProviderCapabilities.LocalFileSystem, left.ProviderCapabilities);
        Assert.Equal("found.txt", left.Items[left.CursorIndex].Name);
    }

    [Fact]
    public void Run_StopDuringSearchReturnsToActivePanelWithoutPartialResults()
    {
        string localFile = Path.Combine(_root, "local.txt");
        string subDirectory = Path.Combine(_root, "sub");
        string foundFile = Path.Combine(subDirectory, "found.txt");
        var fileOps = new RecordingFileOperationService();
        var service = new BlockingSearchService(SearchResult(foundFile));
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.F7, alt: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(KeyChar('S', ConsoleKey.S));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var fs = CreateFileSystem(new FilePanelItem
        {
            Name = "local.txt",
            FullPath = localFile,
            IsDirectory = false,
            Size = 1,
            LastWriteTime = new DateTime(2026, 1, 1),
        });

        var app = CreateApp(fs, driver, fileOps, service);

        app.Run();

        var left = GetLeftPanel(app);
        Assert.Equal(_root, left.CurrentDirectory);
        Assert.Equal(PanelProviderCapabilities.LocalFileSystem, left.ProviderCapabilities);
        Assert.Null(left.SearchRequest);
        Assert.DoesNotContain(left.Items, item => item.FullPath == foundFile);
        Assert.True(service.CancellationObserved);
    }

    private FakeFileSystemService CreateFileSystem(params FilePanelItem[] items)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root, items);
        return fs;
    }

    private Application CreateApp(
        FakeFileSystemService fs,
        FakeConsoleDriver driver,
        RecordingFileOperationService fileOps,
        ISearchService? searchService = null,
        IFileLauncher? fileLauncher = null)
    {
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            fileOps,
            new InMemoryHistoryStore(),
            settings,
            fileLauncher: fileLauncher,
            searchService: searchService ?? new EmptySearchService());
    }

    private void SetSearchResultsPanel(FilePanelState state, string fullPath, bool isDirectory = false)
    {
        state.CurrentDirectory = _root;
        state.Items.Clear();
        state.Items.Add(new FilePanelItem
        {
            Name = Path.GetFileName(fullPath),
            FullPath = fullPath,
            IsDirectory = isDirectory,
            Size = isDirectory ? null : 1,
            LastWriteTime = new DateTime(2026, 1, 1),
            Attributes = isDirectory ? FileAttributes.Directory : FileAttributes.Archive,
        });
        state.SelectedPaths.Clear();
        state.CursorIndex = 0;
        state.ScrollOffset = 0;
        state.ProviderCapabilities = PanelProviderCapabilities.SearchResults;
        state.DisplayTitle = "Search results: *.txt";
        state.ShowCurrentItemFullPath = true;
        state.SearchRequest = new SearchRequest
        {
            RootPath = _root,
            FileMaskExpression = "*.txt",
            Scope = SearchScope.CurrentDirectoryRecursive,
            MaxDegreeOfParallelism = 1,
        };
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, bool alt = false) =>
        new('\0', key, shift: false, alt: alt, control: false);

    private static ConsoleKeyInfo KeyChar(char keyChar, ConsoleKey key) =>
        new(keyChar, key, shift: false, alt: false, control: false);

    private static SearchResultItem SearchResult(string fullPath) =>
        new()
        {
            FullPath = fullPath,
            Name = Path.GetFileName(fullPath),
            Kind = SearchResultItemKind.File,
            Size = 1,
            LastWriteTime = new DateTime(2026, 1, 1),
            Attributes = FileAttributes.Archive,
        };

    private static FilePanelState GetLeftPanel(Application app) =>
        GetPanel(app, "_left");

    private static FilePanelState GetRightPanel(Application app) =>
        GetPanel(app, "_right");

    private static FilePanelState GetPanel(Application app, string fieldName)
    {
        var field = typeof(Application).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Application.{fieldName} field not found.");

        return (FilePanelState)field.GetValue(app)!;
    }

    private sealed class NoOpShellService : IShellService
    {
        public void Execute(string command, string workingDirectory) { }
    }

    private sealed class RecordingFileOperationService : IFileOperationService
    {
        public List<FileOperationRequest> Requests { get; } = [];

        public Task<FileOperationResult> ExecuteAsync(
            FileOperationRequest request,
            IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new FileOperationResult { Kind = request.Kind, Errors = [] });
        }
    }

    private sealed class RecordingFileLauncher : IFileLauncher
    {
        public List<string> OpenedFiles { get; } = [];

        public FileLaunchMode GetLaunchMode(string fullPath) => FileLaunchMode.ShellAssociation;

        public void OpenFile(string fullPath, string workingDirectory) => OpenedFiles.Add(fullPath);
    }

    private sealed class EmptySearchService : ISearchService
    {
        public async IAsyncEnumerable<SearchResultItem> SearchAsync(
            SearchRequest request,
            IProgress<SearchProgress>? progress,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class BlockingSearchService : ISearchService
    {
        private readonly SearchResultItem _item;

        public BlockingSearchService(SearchResultItem item) => _item = item;

        public bool CancellationObserved { get; private set; }

        public async IAsyncEnumerable<SearchResultItem> SearchAsync(
            SearchRequest request,
            IProgress<SearchProgress>? progress,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            progress?.Report(new SearchProgress
            {
                CurrentPath = request.RootPath,
                ScannedFiles = 1,
                MatchedItems = 1,
            });

            yield return _item;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
