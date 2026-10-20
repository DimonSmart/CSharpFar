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
    public void Run_HiddenCommandLineOriginOnlyViewportChange_DoesNotRepaint()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.SetBufferHeight(20);
        driver.TryScrollViewportToBottom();
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.ClearRecordedOperations();
                afterHide.SetViewportOrigin(0, 4);
                afterHide.BeforeReadInput = afterScroll =>
                {
                    Assert.Equal(4, afterScroll.GetViewport().Top);
                    Assert.Equal(0, afterScroll.WriteAtCallCount);
                    Assert.Equal(0, afterScroll.ClearRegionCallCount);
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

    [Fact]
    public void Run_HiddenCommandLineSizeChange_RedrawsCommandLine()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.ClearRecordedOperations();
                afterHide.SetSize(50, 9);
                afterHide.BeforeReadInput = afterResize =>
                {
                    Assert.True(afterResize.WriteAtCallCount > 0);
                    Assert.Equal(7, afterResize.CursorY);
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedHiddenVerticalGrowWhilePinned_KeepsViewportAtBottom()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 20) { IsSupported = true };
        driver.SetBufferHeight(100);
        driver.SetViewportOrigin(0, 80);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        BeforeEachRead(
            driver,
            _ => { },
            afterHide =>
            {
                afterHide.WriteAt(0, 0, "COMMAND-OUTPUT".AsSpan());
                afterHide.ClearRecordedOperations();
                afterHide.SetSize(40, 30);
            },
            afterResize =>
            {
                Assert.Equal(70, afterResize.GetViewport().Top);
                Assert.Equal(1, afterResize.TryScrollViewportToBottomCallCount);
                Assert.True(afterResize.WriteAtCallCount > 0);
                Assert.StartsWith("COMMAND-OUTPUT", afterResize.GetRow(0));
            });

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedHiddenVerticalShrinkWhilePinned_KeepsViewportAtBottom()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 30) { IsSupported = true };
        driver.SetBufferHeight(100);
        driver.SetViewportOrigin(0, 70);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        BeforeEachRead(
            driver,
            _ => { },
            afterHide =>
            {
                afterHide.ClearRecordedOperations();
                afterHide.SetSize(40, 15);
            },
            afterResize =>
            {
                Assert.Equal(85, afterResize.GetViewport().Top);
                Assert.Equal(1, afterResize.TryScrollViewportToBottomCallCount);
                Assert.True(afterResize.WriteAtCallCount > 0);
            });

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedHiddenResizeWhileViewingScrollback_DoesNotForceBottom()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 20) { IsSupported = true };
        driver.SetBufferHeight(100);
        driver.SetViewportOrigin(0, 80);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        BeforeEachRead(
            driver,
            _ => { },
            afterHide => afterHide.SetViewportOrigin(0, 40),
            afterOriginOnly =>
            {
                Assert.Equal(40, afterOriginOnly.GetViewport().Top);
                afterOriginOnly.ClearRecordedOperations();
                afterOriginOnly.SetSize(40, 30);
            },
            afterResize =>
            {
                Assert.Equal(40, afterResize.GetViewport().Top);
                Assert.Equal(0, afterResize.TryScrollViewportToBottomCallCount);
                Assert.True(afterResize.WriteAtCallCount > 0);
            });

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedHiddenOriginOnlyScrollBackToBottom_UpdatesPinnedState()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 20) { IsSupported = true };
        driver.SetBufferHeight(100);
        driver.SetViewportOrigin(0, 80);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        BeforeEachRead(
            driver,
            _ => { },
            afterHide =>
            {
                afterHide.ClearRecordedOperations();
                afterHide.SetViewportOrigin(0, 40);
            },
            afterScrollUp =>
            {
                Assert.Equal(40, afterScrollUp.GetViewport().Top);
                Assert.Equal(0, afterScrollUp.WriteAtCallCount);
                afterScrollUp.SetViewportOrigin(0, 80);
            },
            afterScrollToBottom =>
            {
                Assert.Equal(80, afterScrollToBottom.GetViewport().Top);
                afterScrollToBottom.ClearRecordedOperations();
                afterScrollToBottom.SetSize(40, 30);
            },
            afterResize =>
            {
                Assert.Equal(70, afterResize.GetViewport().Top);
                Assert.Equal(1, afterResize.TryScrollViewportToBottomCallCount);
            });

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_VisiblePanelsResize_DoesNotRunHiddenBottomAnchorLogic()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 20) { IsSupported = true };
        driver.SetBufferHeight(100);
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        BeforeEachRead(
            driver,
            beforeResize =>
            {
                beforeResize.ClearRecordedOperations();
                beforeResize.SetSize(40, 30);
            },
            afterResize =>
            {
                Assert.True(afterResize.IsApplicationScreenActive);
                Assert.Equal(0, afterResize.TryScrollViewportToBottomCallCount);
                Assert.True(afterResize.WriteAtCallCount > 0);
            });

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_HiddenCommandLineInterruptedResize_RestoresUnderlayBeforeEveryAttempt()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.SetSize(40, 9);
                afterHide.ClearRecordedOperations();

                bool interrupted = false;
                afterHide.Wrote += _ =>
                {
                    if (interrupted)
                        return;

                    interrupted = true;
                    afterHide.SetSize(40, 8);
                };

                afterHide.BeforeReadInput = afterResize =>
                {
                    Assert.True(interrupted);
                    Assert.Equal(2, afterResize.RestoreCallCount);
                    Assert.Contains(">", afterResize.GetRow(6), StringComparison.Ordinal);
                    Assert.DoesNotContain(">", afterResize.GetRow(7), StringComparison.Ordinal);
                    Assert.Equal(6, afterResize.CursorY);
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedHiddenCommandLineInterruptedResize_PreservesCommandOutput()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8) { IsSupported = true };
        driver.SetBufferHeight(30);
        driver.SetViewportOrigin(0, 22);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.WriteAt(0, 0, "COMMAND-OUTPUT".AsSpan());
                afterHide.SetSize(40, 9);
                afterHide.ClearRecordedOperations();

                bool interrupted = false;
                afterHide.Wrote += _ =>
                {
                    if (interrupted)
                        return;

                    interrupted = true;
                    afterHide.SetSize(40, 10);
                    afterHide.WriteAt(0, 0, "COMMAND-OUTPUT".AsSpan());
                };

                afterHide.BeforeReadInput = afterResize =>
                {
                    Assert.True(interrupted);
                    Assert.True(afterResize.OperationLog.Count(operation => operation == "Capture") >= 2);
                    Assert.Equal(20, afterResize.GetViewport().Top);
                    Assert.StartsWith("COMMAND-OUTPUT", afterResize.GetRow(0));
                    Assert.Equal([8], RowsContainingCommandPrompt(afterResize));
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedHiddenResizeDuringDrain_RendersAgainForFinalViewport()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 20) { IsSupported = true };
        driver.SetBufferHeight(100);
        driver.SetViewportOrigin(0, 80);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.ClearRecordedOperations();
                afterHide.SetSize(40, 19);
                afterHide.BeforeTryReadInput = duringDrain =>
                {
                    duringDrain.SetSize(40, 18);
                    duringDrain.EnqueueInput(new ConsoleResizeInputEvent());
                };

                afterHide.BeforeReadInput = afterResize =>
                {
                    Assert.Equal(82, afterResize.GetViewport().Top);
                    Assert.Equal([16], RowsContainingCommandPrompt(afterResize));
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedHiddenResizeStartedPinned_DoesNotLearnIntermediateUnpinnedState()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 20) { IsSupported = true };
        driver.SetBufferHeight(100);
        driver.SetViewportOrigin(0, 80);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.ClearRecordedOperations();
                afterHide.SetSize(40, 19);
                afterHide.BeforeTryReadInput = duringDrain =>
                    duringDrain.SetViewportOrigin(0, 70);

                afterHide.BeforeReadInput = afterResize =>
                {
                    Assert.Equal(81, afterResize.GetViewport().Top);
                    Assert.Equal([17], RowsContainingCommandPrompt(afterResize));
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_HiddenResizeShowGrowHide_DoesNotLeaveStaleCommandLine()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        int oldRow = ApplicationLayoutService.CommandLineRow(new ConsoleSize(80, 8));
        int finalRow = ApplicationLayoutService.CommandLineRow(new ConsoleSize(80, 16));
        BeforeEachRead(
            driver,
            _ => { },
            afterHide => afterHide.SetSize(80, 8),
            _ => { },
            afterShow => afterShow.SetSize(80, 16),
            _ => { },
            afterSecondHide =>
            {
                var promptRows = RowsContainingCommandPrompt(afterSecondHide);
                Assert.Equal([finalRow], promptRows);
                Assert.DoesNotContain(">", afterSecondHide.GetRow(oldRow), StringComparison.Ordinal);
            });

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

    [Fact]
    public void Run_VtSupportedHiddenResizeShowGrowHide_DoesNotLeaveStaleCommandLine()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12) { IsSupported = true };
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        int oldRow = ApplicationLayoutService.CommandLineRow(new ConsoleSize(80, 8));
        int finalRow = ApplicationLayoutService.CommandLineRow(new ConsoleSize(80, 16));
        BeforeEachRead(
            driver,
            _ => { },
            afterHide => afterHide.SetSize(80, 8),
            _ => { },
            afterShow =>
            {
                Assert.True(afterShow.IsApplicationScreenActive);
                afterShow.SetSize(80, 16);
            },
            _ => { },
            afterSecondHide =>
            {
                Assert.False(afterSecondHide.IsApplicationScreenActive);
                Assert.True(afterSecondHide.LeaveApplicationScreenCallCount > 0);
                Assert.True(afterSecondHide.EnterApplicationScreenCallCount > 0);
                var promptRows = RowsContainingCommandPrompt(afterSecondHide);
                Assert.Equal([finalRow], promptRows);
                Assert.DoesNotContain(">", afterSecondHide.GetRow(oldRow), StringComparison.Ordinal);
            });

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();
    }

    [Fact]
    public void Run_HiddenCommandLineResizeBurst_CoalescesToOneStablePrompt()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 120, height: 30) { IsSupported = true };
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));

        int finalRow = ApplicationLayoutService.CommandLineRow(new ConsoleSize(120, 30));
        BeforeEachRead(
            driver,
            _ => { },
            afterHide =>
            {
                afterHide.ClearRegion(new Rect(0, finalRow, 120, 1));
                afterHide.ClearRecordedOperations();
                var widths = new Queue<int>([108, 104, 108, 120]);
                afterHide.BeforeGetViewport = sample =>
                {
                    if (widths.TryDequeue(out int width))
                        sample.SetSize(width, 30);
                    else
                        sample.BeforeGetViewport = null;
                };
            });

        var app = CreateApp(fs, driver, _tempDir, terminalScreenMode: driver);
        app.Run();

        Assert.Equal(new ConsoleSize(120, 30), driver.GetSize());
        Assert.Equal(finalRow, driver.CursorY);
        Assert.Equal(1, driver.TrySetCursorPositionInViewportCallCount);
        Assert.Equal(0, driver.PendingInputCount);
    }

}
