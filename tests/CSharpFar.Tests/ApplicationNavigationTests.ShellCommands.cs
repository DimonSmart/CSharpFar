using System.Reflection;
using CSharpFar.App;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed partial class ApplicationNavigationTests
{
    [Fact]
    public void CtrlOAfterCommandExecution_RestoresCommandOutputUnderlay()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.SetBufferHeight(30);
        var app = CreateApp(fs, driver, _tempDir);

        Render(app);
        app.ExecuteInCurrentConsole(
            _tempDir,
            "test",
            () => driver.WriteAt(0, 0, "COMMAND-OUTPUT".AsSpan()));
        Render(app);

        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));

        Assert.StartsWith("COMMAND-OUTPUT", driver.GetRow(0));
    }

    [Fact]
    public void CtrlOAfterCommandExecutionAndResize_RestoresClippedCommandOutputUnderlay()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        Render(app);
        app.ExecuteInCurrentConsole(
            _tempDir,
            "test",
            () => driver.WriteAt(0, 0, "COMMAND-OUTPUT-AFTER-RESIZE".AsSpan()));
        Render(app);

        driver.SetSize(18, 8);
        Render(app);

        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));

        Assert.Equal("COMMAND-OUTPUT-AFT", driver.GetRow(0));
    }

    [Fact]
    public void ExecuteCommand_CdExistingSubFolder_LoadsActivePanelWithoutShell()
    {
        string child = Directory.CreateDirectory(Path.Combine(_tempDir, "child")).FullName;
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);
        fs.AddDirectory(child);

        var shell = new RecordingShellService();
        var app = CreateApp(fs, new FakeConsoleDriver(width: 80, height: 12), _tempDir, shell);

        app.ExecuteCommand("cd child");

        Assert.Equal(child, GetLeftPanel(app).CurrentDirectory);
        Assert.Empty(shell.Commands);
    }

    [Fact]
    public void ExecuteCommand_Dir_RunsShellAndLeavesActivePanel()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var shell = new RecordingShellService();
        var app = CreateApp(fs, new FakeConsoleDriver(width: 80, height: 12), _tempDir, shell);

        app.ExecuteCommand("dir");

        Assert.Equal(_tempDir, GetLeftPanel(app).CurrentDirectory);
        Assert.Equal([("dir", _tempDir)], shell.Commands);
    }

    [Fact]
    public void ExecuteCommand_WithQuotedArguments_PreservesQuotesForShell()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var shell = new RecordingShellService();
        var app = CreateApp(fs, new FakeConsoleDriver(width: 80, height: 12), _tempDir, shell);

        app.ExecuteCommand("git commit -m \"Initial commit\"");

        Assert.Equal([("git commit -m \"Initial commit\"", _tempDir)], shell.Commands);
    }

    [Fact]
    public void ExecuteCommand_Dir_UsesChildProcessConsoleModeDuringShellExecution()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        bool childModeDuringShell = false;
        var shell = new RecordingShellService((_, _) =>
        {
            childModeDuringShell = driver.ChildProcessConsoleMode;
        });
        var app = CreateApp(fs, driver, _tempDir, shell);

        app.ExecuteCommand("dir");

        Assert.True(childModeDuringShell);
        Assert.False(driver.ChildProcessConsoleMode);
        Assert.Equal(1, driver.EnterChildProcessConsoleModeCallCount);
        Assert.True(driver.RestoreApplicationInputModeCallCount >= 1);
    }

    [Fact]
    public void ExecuteCommand_CdMissingFolder_LeavesActivePanelWithoutShell()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var shell = new RecordingShellService();
        var app = CreateApp(fs, new FakeConsoleDriver(width: 80, height: 12), _tempDir, shell);

        app.ExecuteCommand("cd missing");

        Assert.Equal(_tempDir, GetLeftPanel(app).CurrentDirectory);
        Assert.Empty(shell.Commands);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 5)]
    public void Run_NarrowConsole_DoesNotThrow(int width, int height)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width, height);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(fs, driver, _tempDir);

        app.Run();
    }

}
