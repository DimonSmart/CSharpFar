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
    public void Run_HiddenCommandLineInputAfterScroll_ReturnsViewportToBottom()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.SetBufferHeight(20);
        driver.TryScrollViewportToBottom();
        int bottomTop = driver.GetViewport().Top;
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.X, keyChar: 'x'));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.WriteAt(0, 0, "BOTTOM-OUTPUT".AsSpan());
                afterHide.SetViewportOrigin(0, 4);
                afterHide.BeforeReadInput = beforeInput =>
                {
                    Assert.Equal(4, beforeInput.GetViewport().Top);
                    beforeInput.BeforeReadInput = afterInput =>
                    {
                        Assert.Equal(bottomTop, afterInput.GetViewport().Top);
                        Assert.StartsWith("BOTTOM-OUTPUT", afterInput.GetRow(0));
                    };
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();

        Assert.Equal("x", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_VtSupportedHiddenCommandLinePureModifierAfterScroll_DoesNotScrollOrRender()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8) { IsSupported = true };
        driver.SetBufferHeight(20);
        driver.TryScrollViewportToBottom();
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Alt));
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(default));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        int read = 0;
        OnEveryRead(driver, current =>
        {
            switch (read++)
            {
                case 1:
                    current.SetViewportOrigin(0, 4);
                    current.ClearRecordedOperations();
                    break;
                case 2:
                    Assert.Equal(FunctionKeyLayer.Control, app.Session.FunctionKeyLayer);
                    AssertNoHiddenModifierSideEffects(current);
                    break;
                case 3:
                    Assert.Equal(FunctionKeyLayer.Alt, app.Session.FunctionKeyLayer);
                    AssertNoHiddenModifierSideEffects(current);
                    break;
                case 4:
                    Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
                    AssertNoHiddenModifierSideEffects(current);
                    break;
            }
        });

        app.Run();

        Assert.Equal(5, read);
        Assert.Equal(string.Empty, GetCommandLine(app).Text);

        void AssertNoHiddenModifierSideEffects(FakeConsoleDriver current)
        {
            Assert.Equal(ApplicationWorkspaceMode.HiddenCommandLine, app.Session.App.WorkspaceMode);
            Assert.Equal(4, current.GetViewport().Top);
            Assert.Equal(0, current.TryScrollViewportToBottomCallCount);
            Assert.Equal(0, current.WriteAtCallCount);
            Assert.Equal(0, current.ClearRegionCallCount);
            Assert.DoesNotContain("Capture", current.OperationLog);
        }
    }

    [Fact]
    public void Run_VtSupportedHiddenCommandLineInputAfterScroll_CapturesBeforeCommandLineRender()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8) { IsSupported = true };
        driver.SetBufferHeight(20);
        driver.TryScrollViewportToBottom();
        int bottomTop = driver.GetViewport().Top;
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.X, keyChar: 'x'));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.WriteAt(0, 0, "BOTTOM-OUTPUT".AsSpan());
                afterHide.SetViewportOrigin(0, 4);
                afterHide.BeforeReadInput = beforeInput =>
                {
                    Assert.Equal(4, beforeInput.GetViewport().Top);
                    beforeInput.ClearRecordedOperations();
                    beforeInput.BeforeReadInput = afterInput =>
                    {
                        Assert.Equal(bottomTop, afterInput.GetViewport().Top);
                        AssertOperationOrder(
                            afterInput,
                            "TryScrollViewportToBottom",
                            "Capture",
                            "WriteAt");
                        Assert.Equal(0, afterInput.ClearRegionCallCount);
                    };
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();

        Assert.Equal("x", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_CtrlOFromScrolledHiddenCommandLine_ReturnsViewportToBottomAndRendersPanels()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.SetBufferHeight(20);
        driver.TryScrollViewportToBottom();
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.SetViewportOrigin(0, 4);
                afterHide.BeforeReadInput = beforeShow =>
                {
                    Assert.Equal(4, beforeShow.GetViewport().Top);
                    beforeShow.ClearRecordedOperations();
                    beforeShow.BeforeReadInput = afterShow =>
                    {
                        Assert.True(afterShow.TryIsViewportAtBottom(out bool isAtBottom));
                        Assert.True(isAtBottom);
                        Assert.True(afterShow.WriteAtCallCount > 0);
                    };
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

}
