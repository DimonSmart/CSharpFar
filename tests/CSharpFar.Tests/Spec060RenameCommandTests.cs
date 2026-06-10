using CSharpFar.App;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec060RenameCommandTests : IDisposable
{
    private readonly string _root;

    public Spec060RenameCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec060_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Run_ShiftF6RenamesCurrentItemAndIgnoresSelection()
    {
        string currentPath = Path.Combine(_root, "current.txt");
        string selectedPath = Path.Combine(_root, "selected.txt");
        var fs = new FakeFileSystemService();
        fs.AddDirectory(
            _root,
            Item("current.txt", currentPath),
            Item("selected.txt", selectedPath));
        var fileOperations = new RecordingFileOperationService();
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.F6, shift: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true));
        EnqueueText(driver, "renamed.txt");
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, fileOperations, driver);
        GetLeftPanel(app).CursorIndex = 1;
        GetLeftPanel(app).SelectedPaths.Add(selectedPath);
        GetLeftPanel(app).SelectedLocations.Add(PanelLocation.Local(selectedPath));

        app.Run();

        FileOperationRequest request = Assert.Single(fileOperations.Requests);
        Assert.Equal(FileOperationKind.Move, request.Kind);
        Assert.Equal([currentPath], request.Sources);
        Assert.Equal("renamed.txt", request.Destination);
    }

    private Application CreateApp(
        FakeFileSystemService fs,
        IFileOperationService fileOperations,
        FakeConsoleDriver driver)
    {
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            fileOperations,
            new InMemoryHistoryStore(),
            settings);
    }

    private static FilePanelItem Item(string name, string path) =>
        new()
        {
            Name = name,
            FullPath = path,
            IsDirectory = false,
            Size = 1,
            LastWriteTime = new DateTime(2026, 1, 1),
            Attributes = FileAttributes.Archive,
        };

    private static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char ch in text)
            driver.EnqueueKey(new ConsoleKeyInfo(ch, ConsoleKey.None, shift: false, alt: false, control: false));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, bool shift = false) =>
        new('\0', key, shift, alt: false, control: false);

    private static FilePanelState GetLeftPanel(Application app)
    {
        return app.Session.Panels.Left;
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
}
