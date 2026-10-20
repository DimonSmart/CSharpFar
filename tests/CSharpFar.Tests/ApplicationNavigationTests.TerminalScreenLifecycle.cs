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
    public void Run_WorkspaceModeControlsConsoleScrollback()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = beforeHide =>
        {
            Assert.False(beforeHide.ConsoleScrollbackEnabled);
            beforeHide.BeforeReadInput = afterHide =>
            {
                Assert.True(afterHide.ConsoleScrollbackEnabled);
                afterHide.BeforeReadInput = afterShow =>
                    Assert.False(afterShow.ConsoleScrollbackEnabled);
            };
        };

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedHidingBothPanels_LeavesApplicationScreenWithoutLegacyScrollback()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12) { IsSupported = true };
        driver.SetBufferHeight(30);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = beforeHide =>
        {
            Assert.True(beforeHide.IsApplicationScreenActive);
            beforeHide.ClearRecordedOperations();
            beforeHide.BeforeReadInput = afterHide =>
            {
                Assert.False(afterHide.IsApplicationScreenActive);
                Assert.Equal(1, afterHide.TryScrollViewportToBottomCallCount);
                Assert.Equal(18, afterHide.GetViewport().Top);
                AssertOperationOrder(
                    afterHide,
                    "LeaveApplicationScreen",
                    "TryScrollViewportToBottom",
                    "Capture");
                Assert.Equal(0, afterHide.ClearRegionCallCount);
                Assert.Equal(0, afterHide.SetConsoleScrollbackEnabledCallCount);
            };
        };

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedShowingPanelsAgain_EntersApplicationScreen()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12) { IsSupported = true };
        driver.SetBufferHeight(30);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = beforeHide =>
        {
            Assert.True(beforeHide.IsApplicationScreenActive);
            beforeHide.ClearRecordedOperations();
            beforeHide.BeforeReadInput = afterHide =>
            {
                Assert.False(afterHide.IsApplicationScreenActive);
                Assert.Equal(1, afterHide.TryScrollViewportToBottomCallCount);
                AssertOperationOrder(
                    afterHide,
                    "LeaveApplicationScreen",
                    "TryScrollViewportToBottom",
                    "Capture");
                afterHide.BeforeReadInput = afterShow =>
                {
                    Assert.True(afterShow.IsApplicationScreenActive);
                    int scrollsAfterShow = afterShow.TryScrollViewportToBottomCallCount;
                    afterShow.ClearRecordedOperations();
                    afterShow.BeforeReadInput = afterSecondHide =>
                    {
                        Assert.False(afterSecondHide.IsApplicationScreenActive);
                        Assert.Equal(1, afterSecondHide.TryScrollViewportToBottomCallCount);
                        Assert.Equal(18, afterSecondHide.GetViewport().Top);
                        AssertOperationOrder(
                            afterSecondHide,
                            "LeaveApplicationScreen",
                            "TryScrollViewportToBottom",
                            "Capture");
                        Assert.Equal(0, afterSecondHide.ClearRegionCallCount);
                    };
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedControlF1AndF2RemainInApplicationScreen()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12) { IsSupported = true };
        driver.SetBufferHeight(30);
        driver.EnqueueKey(Key(ConsoleKey.F1, control: true));
        driver.EnqueueKey(Key(ConsoleKey.F2, control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = beforeControlF1 =>
        {
            Assert.True(beforeControlF1.IsApplicationScreenActive);
            beforeControlF1.BeforeReadInput = afterControlF1 =>
            {
                Assert.True(afterControlF1.IsApplicationScreenActive);
                Assert.Equal(1, afterControlF1.EnterApplicationScreenCallCount);
                Assert.Equal(0, afterControlF1.SetConsoleScrollbackEnabledCallCount);
                Assert.Equal(0, afterControlF1.TryScrollViewportToBottomCallCount);
                afterControlF1.ClearRecordedOperations();
                afterControlF1.BeforeReadInput = afterControlF2 =>
                {
                    Assert.True(afterControlF2.IsApplicationScreenActive);
                    Assert.Equal(0, afterControlF2.TryScrollViewportToBottomCallCount);
                    Assert.Equal(0, afterControlF2.ClearRegionCallCount);
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void RenderCommandLineOnly_VtSupportedHiddenCommandLine_DoesNotAutoScroll()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12) { IsSupported = true };
        driver.SetBufferHeight(30);
        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);

        Render(app);
        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.ClearRecordedOperations();
        driver.SetViewportOrigin(0, 4);

        RenderCommandLineOnly(app);

        Assert.Equal(4, driver.GetViewport().Top);
        Assert.Equal(0, driver.TryScrollViewportToBottomCallCount);
        Assert.Contains(">", driver.GetRow(ApplicationLayoutService.CommandLineRow(driver.GetSize())), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_VtSupportedHideAfterCommandOutput_CapturesMainViewportBeforeCommandLineRender()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12) { IsSupported = true };
        driver.SetBufferHeight(30);
        var shell = new RecordingShellService((_, _) =>
            driver.WriteAt(0, 0, "COMMAND-OUTPUT".AsSpan()));
        var app = CreateApp(fs, driver, _tempDir, shell, terminalScreenMode: driver);

        Render(app);
        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        app.ExecuteCommand("dir");
        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        HandleKeyAndRender(app, Key(ConsoleKey.A, keyChar: 'a'));
        driver.ClearRecordedOperations();

        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));

        Assert.False(driver.IsApplicationScreenActive);
        Assert.Equal(1, driver.TryScrollViewportToBottomCallCount);
        AssertOperationOrder(
            driver,
            "LeaveApplicationScreen",
            "TryScrollViewportToBottom",
            "Capture");
        Assert.Equal(0, driver.ClearRegionCallCount);
        Assert.Contains(driver.OperationLog, operation => operation == "Capture");
    }

    [Fact]
    public void ExecuteCommand_VtSupportedVisiblePanels_UsesMainScreenDuringShellThenReturnsToApplicationScreen()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12) { IsSupported = true };
        driver.SetBufferHeight(30);
        bool mainScreenDuringShell = false;
        var shell = new RecordingShellService((_, _) =>
        {
            mainScreenDuringShell = !driver.IsApplicationScreenActive;
            Assert.Equal(1, driver.TryScrollViewportToBottomCallCount);
            Assert.Equal(18, driver.GetViewport().Top);
            AssertOperationOrder(
                driver,
                "LeaveApplicationScreen",
                "TryScrollViewportToBottom",
                "Capture",
                "WriteAt");
        });
        var app = CreateApp(fs, driver, _tempDir, shell, terminalScreenMode: driver);

        Render(app);
        driver.ClearRecordedOperations();
        app.ExecuteCommand("dir");

        Assert.True(mainScreenDuringShell);
        Assert.True(driver.IsApplicationScreenActive);
        Assert.True(driver.LeaveApplicationScreenCallCount >= 1);
        Assert.Equal(0, driver.SetConsoleScrollbackEnabledCallCount);
    }

    [Fact]
    public void ExecuteCommand_VtSupportedHiddenCommandLine_StaysInMainScreenAfterShell()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12) { IsSupported = true };
        var shell = new RecordingShellService();
        var app = CreateApp(fs, driver, _tempDir, shell, terminalScreenMode: driver);

        Render(app);
        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        app.ExecuteCommand("dir");

        Assert.False(driver.IsApplicationScreenActive);
        Assert.Equal(0, driver.SetConsoleScrollbackEnabledCallCount);
    }

    [Fact]
    public void Run_VtSupportedExit_RestoresTerminal()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12) { IsSupported = true };
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
        driver.RestoreTerminal();

        Assert.False(driver.IsApplicationScreenActive);
        Assert.True(driver.RestoreTerminalCallCount >= 2);
    }

    [Fact]
    public void Run_VtUnsupported_UsesLegacyScrollbackFallback()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12) { IsSupported = false };
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = beforeHide =>
        {
            Assert.False(beforeHide.ConsoleScrollbackEnabled);
            beforeHide.BeforeReadInput = afterHide =>
                Assert.True(afterHide.ConsoleScrollbackEnabled);
        };

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();

        Assert.Equal(0, driver.EnterApplicationScreenCallCount);
        Assert.True(driver.SetConsoleScrollbackEnabledCallCount > 0);
    }

}
