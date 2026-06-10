using System.Diagnostics;
using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Shell;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec009OpenPanelItemTests : IDisposable
{
    private readonly string _root;

    public Spec009OpenPanelItemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec009_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Run_EnterOnFile_OpensThroughFileLauncher()
    {
        string filePath = Path.Combine(_root, "note.txt");
        var fs = CreateFileSystem(new FilePanelItem
        {
            Name = "note.txt",
            FullPath = filePath,
            IsDirectory = false,
        });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var launcher = new RecordingFileLauncher();
        var app = CreateApp(fs, driver, launcher);

        app.Run();

        Assert.Equal([filePath], launcher.OpenedFiles);
    }

    [Fact]
    public void Run_EnterOnDirectory_NavigatesThroughOpenCurrentItem()
    {
        string childPath = Path.Combine(_root, "child");
        var fs = CreateFileSystem(new FilePanelItem
        {
            Name = "child",
            FullPath = childPath,
            IsDirectory = true,
            Attributes = FileAttributes.Directory,
        });
        fs.AddDirectory(childPath);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver, new RecordingFileLauncher());

        app.Run();

        Assert.Equal(childPath, GetLeftPanel(app).CurrentDirectory);
    }

    [Fact]
    public void Run_DoubleClickOnFile_OpensSameItem()
    {
        string filePath = Path.Combine(_root, "note.txt");
        var fs = CreateFileSystem(new FilePanelItem
        {
            Name = "note.txt",
            FullPath = filePath,
            IsDirectory = false,
        });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueInput(LeftMouse(2, 2, MouseEventKind.Down));
        driver.EnqueueInput(LeftMouse(2, 2, MouseEventKind.DoubleClick));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var launcher = new RecordingFileLauncher();
        var app = CreateApp(fs, driver, launcher);

        app.Run();

        Assert.Equal([filePath], launcher.OpenedFiles);
    }

    [Fact]
    public void Run_DoubleClickOnDirectory_NavigatesToDirectory()
    {
        string childPath = Path.Combine(_root, "child");
        var fs = CreateFileSystem(new FilePanelItem
        {
            Name = "child",
            FullPath = childPath,
            IsDirectory = true,
            Attributes = FileAttributes.Directory,
        });
        fs.AddDirectory(childPath);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueInput(LeftMouse(2, 2, MouseEventKind.Down));
        driver.EnqueueInput(LeftMouse(2, 2, MouseEventKind.DoubleClick));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver, new RecordingFileLauncher());

        app.Run();

        Assert.Equal(childPath, GetLeftPanel(app).CurrentDirectory);
    }

    [Fact]
    public void Run_SingleClickOnFile_SelectsWithoutOpening()
    {
        string filePath = Path.Combine(_root, "note.txt");
        var fs = CreateFileSystem(new FilePanelItem
        {
            Name = "note.txt",
            FullPath = filePath,
            IsDirectory = false,
        });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueInput(LeftMouse(2, 2, MouseEventKind.Down));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var launcher = new RecordingFileLauncher();
        var app = CreateApp(fs, driver, launcher);

        app.Run();

        var left = GetLeftPanel(app);
        Assert.Empty(launcher.OpenedFiles);
        Assert.Equal("note.txt", left.Items[left.CursorIndex].Name);
    }

    [Fact]
    public void Run_DoubleClickAfterDifferentFirstItem_DoesNotOpen()
    {
        string firstPath = Path.Combine(_root, "a.txt");
        string secondPath = Path.Combine(_root, "b.txt");
        var fs = CreateFileSystem(
            new FilePanelItem
            {
                Name = "a.txt",
                FullPath = firstPath,
                IsDirectory = false,
            },
            new FilePanelItem
            {
                Name = "b.txt",
                FullPath = secondPath,
                IsDirectory = false,
            });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueInput(LeftMouse(2, 2, MouseEventKind.Down));
        driver.EnqueueInput(LeftMouse(2, 3, MouseEventKind.DoubleClick));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var launcher = new RecordingFileLauncher();
        var app = CreateApp(fs, driver, launcher);

        app.Run();

        Assert.Empty(launcher.OpenedFiles);
    }

    [Fact]
    public void Run_DoubleClickAfterOutsideFileList_DoesNotUseStaleFirstClick()
    {
        string filePath = Path.Combine(_root, "note.txt");
        var fs = CreateFileSystem(new FilePanelItem
        {
            Name = "note.txt",
            FullPath = filePath,
            IsDirectory = false,
        });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueInput(LeftMouse(2, 2, MouseEventKind.Down));
        driver.EnqueueInput(LeftMouse(2, 8, MouseEventKind.Down));
        driver.EnqueueInput(LeftMouse(2, 2, MouseEventKind.DoubleClick));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var launcher = new RecordingFileLauncher();
        var app = CreateApp(fs, driver, launcher);

        app.Run();

        Assert.Empty(launcher.OpenedFiles);
    }

    [Fact]
    public void Run_EnterOnExecutable_UsesCurrentConsoleLaunchFlow()
    {
        string filePath = Path.Combine(_root, "tool.exe");
        var fs = CreateFileSystem(new FilePanelItem
        {
            Name = "tool.exe",
            FullPath = filePath,
            IsDirectory = false,
        });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var launcher = new RecordingFileLauncher(FileLaunchMode.CurrentConsole, () => driver.RenderingOutputMode);
        var app = CreateApp(fs, driver, launcher);

        app.Run();

        Assert.Equal([filePath], launcher.OpenedFiles);
        Assert.Equal([_root], launcher.WorkingDirectories);
        Assert.Equal([false], launcher.RenderingModesDuringOpen);
        Assert.True(driver.RenderingOutputMode);
    }

    [Fact]
    public void WindowsShellFileLauncher_OpenFile_UsesWindowsShellOpenVerb()
    {
        ProcessStartInfo? captured = null;
        var launcher = new WindowsShellFileLauncher(
            startInfo =>
            {
                captured = startInfo;
                return null;
            },
            _ => { });

        launcher.OpenFile(@"C:\Temp\note.txt", @"C:\Temp");

        Assert.NotNull(captured);
        Assert.Equal(@"C:\Temp\note.txt", captured.FileName);
        Assert.Equal(@"C:\Temp", captured.WorkingDirectory);
        Assert.True(captured.UseShellExecute);
        Assert.Equal("open", captured.Verb);
    }

    [Fact]
    public void WindowsShellFileLauncher_OpenExe_RunsInCurrentConsoleAndWaits()
    {
        ProcessStartInfo? captured = null;
        bool waited = false;
        var launcher = new WindowsShellFileLauncher(
            startInfo =>
            {
                captured = startInfo;
                return new Process();
            },
            _ => waited = true);

        launcher.OpenFile(@"C:\Temp\tool.exe", @"C:\Temp");

        Assert.NotNull(captured);
        Assert.Equal(FileLaunchMode.CurrentConsole, launcher.GetLaunchMode(@"C:\Temp\tool.exe"));
        Assert.Equal(@"C:\Temp\tool.exe", captured.FileName);
        Assert.Equal(@"C:\Temp", captured.WorkingDirectory);
        Assert.False(captured.UseShellExecute);
        Assert.False(captured.RedirectStandardInput);
        Assert.False(captured.RedirectStandardOutput);
        Assert.False(captured.RedirectStandardError);
        Assert.False(captured.CreateNoWindow);
        Assert.Empty(captured.ArgumentList);
        Assert.True(waited);
    }

    [Fact]
    public void WindowsShellFileLauncher_OpenCmd_RunsViaCommandProcessorInCurrentConsole()
    {
        ProcessStartInfo? captured = null;
        bool waited = false;
        var launcher = new WindowsShellFileLauncher(
            startInfo =>
            {
                captured = startInfo;
                return new Process();
            },
            _ => waited = true);

        launcher.OpenFile(@"C:\Temp\build.cmd", @"C:\Temp");

        Assert.NotNull(captured);
        Assert.Equal(FileLaunchMode.CurrentConsole, launcher.GetLaunchMode(@"C:\Temp\build.cmd"));
        Assert.Equal(@"C:\Temp", captured.WorkingDirectory);
        Assert.False(captured.UseShellExecute);
        Assert.Equal(["/c", @"C:\Temp\build.cmd"], captured.ArgumentList);
        Assert.True(waited);
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
        IFileLauncher launcher)
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
            fileLauncher: launcher);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static MouseConsoleInputEvent LeftMouse(int x, int y, MouseEventKind kind) =>
        new(x, y, MouseButton.Left, kind, MouseKeyModifiers.None);

    private static FilePanelState GetLeftPanel(Application app)
    {
        return app.Session.Panels.Left;
    }

    private sealed class RecordingFileLauncher : IFileLauncher
    {
        private readonly FileLaunchMode _launchMode;
        private readonly Func<bool>? _getRenderingOutputMode;
        private readonly List<string> _openedFiles = [];
        private readonly List<string> _workingDirectories = [];
        private readonly List<bool> _renderingModesDuringOpen = [];

        public IReadOnlyList<string> OpenedFiles => _openedFiles;
        public IReadOnlyList<string> WorkingDirectories => _workingDirectories;
        public IReadOnlyList<bool> RenderingModesDuringOpen => _renderingModesDuringOpen;

        public RecordingFileLauncher(
            FileLaunchMode launchMode = FileLaunchMode.ShellAssociation,
            Func<bool>? getRenderingOutputMode = null)
        {
            _launchMode = launchMode;
            _getRenderingOutputMode = getRenderingOutputMode;
        }

        public FileLaunchMode GetLaunchMode(string fullPath) => _launchMode;

        public void OpenFile(string fullPath, string workingDirectory)
        {
            if (_getRenderingOutputMode is not null)
                _renderingModesDuringOpen.Add(_getRenderingOutputMode());

            _openedFiles.Add(fullPath);
            _workingDirectories.Add(workingDirectory);
        }
    }
}
