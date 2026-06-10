using System.Reflection;
using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec036NavigateToRootTests : IDisposable
{
    private readonly string _root;
    private readonly string _subDir;

    public Spec036NavigateToRootTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec036_{Guid.NewGuid():N}");
        _subDir = Path.Combine(_root, "sub", "deep");
        Directory.CreateDirectory(_subDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Run_CtrlBackslash_NavigatesActivePanelToDriveRoot()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_subDir);
        fs.AddDirectory(_root);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(new ConsoleKeyInfo('\u001c', ConsoleKey.Oem5, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(fs, driver, _subDir);
        app.Run();

        var expected = Path.GetPathRoot(_subDir)!;
        Assert.Equal(expected, GetLeftPanel(app).CurrentDirectory);
    }

    [Fact]
    public void Run_CtrlBackslash_WhenAlreadyAtRoot_DoesNotThrow()
    {
        var rootPath = Path.GetPathRoot(_subDir)!;

        var fs = new FakeFileSystemService();
        fs.AddDirectory(rootPath);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(new ConsoleKeyInfo('\u001c', ConsoleKey.Oem5, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(fs, driver, rootPath);
        app.Run();

        Assert.Equal(rootPath, GetLeftPanel(app).CurrentDirectory);
    }

    private Application CreateApp(FakeFileSystemService fs, FakeConsoleDriver driver, string startDirectory)
    {
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = startDirectory;
        settings.Panels.RightStartDirectory = startDirectory;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings);
    }

    private static FilePanelState GetLeftPanel(Application app)
    {
        return app.Session.Panels.Left;
    }
}
