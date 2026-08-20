using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public class ScreenRendererTests
{
    private static (ScreenRenderer renderer, FakeConsoleDriver driver) Create(int w = 80, int h = 25)
    {
        var driver = new FakeConsoleDriver(w, h);
        return (new ScreenRenderer(driver), driver);
    }

    [Fact]
    public void Write_PlacesTextAtPosition()
    {
        var (renderer, driver) = Create();
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        renderer.Write(10, 5, "Test", style);

        Assert.Equal('T', driver.GetCell(10, 5).Character);
        Assert.Equal('e', driver.GetCell(11, 5).Character);
        Assert.Equal('s', driver.GetCell(12, 5).Character);
        Assert.Equal('t', driver.GetCell(13, 5).Character);
        Assert.Equal(ConsoleColor.White, driver.GetCell(10, 5).Foreground);
        Assert.Equal(ConsoleColor.DarkBlue, driver.GetCell(10, 5).Background);
    }

    [Fact]
    public void ConsoleTextMetrics_UsesTerminalCellsAndKeepsUnicodeScalarsWhole()
    {
        const string text = "A界🙂e\u0301";

        Assert.Equal(6, ConsoleTextMetrics.GetCellWidth(text));
        Assert.Equal("A界", ConsoleTextMetrics.TruncateToCells(text, 3));
        Assert.Equal("A界 ", ConsoleTextMetrics.FitToCells(text, 4));
        Assert.Equal(2, ConsoleTextMetrics.Utf16IndexFromCellOffset(text, 3));
        Assert.Equal(3, ConsoleTextMetrics.CellOffsetFromUtf16Index(text, 3));
    }

    [Fact]
    public void ClippedCanvas_ConstrainsTextAndNeverDrawsPartOfWideRune()
    {
        var (renderer, driver) = Create(6, 2);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        UiTestRender.Render(renderer, canvas =>
        {
            canvas.FillRegion(new Rect(0, 0, 6, 2), CellStyle.Default);
            canvas.Clip(new Rect(2, 0, 2, 1)).Write(1, 0, "界AB", style);
        });

        Assert.Equal(' ', driver.GetCell(2, 0).Character);
        Assert.Equal('A', driver.GetCell(3, 0).Character);
        Assert.Equal(' ', driver.GetCell(4, 0).Character);
    }

    [Fact]
    public void ClippedCanvas_FillAndBoxKeepOriginalGeometryInsideClip()
    {
        var (renderer, driver) = Create(8, 6);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        UiTestRender.Render(renderer, canvas =>
        {
            canvas.FillRegion(new Rect(0, 0, 8, 6), CellStyle.Default);
            IUiCanvas clipped = canvas.Clip(new Rect(3, 1, 2, 4));
            clipped.FillRegion(new Rect(1, 0, 6, 6), style);
            clipped.DrawBox(new Rect(1, 1, 6, 4), style);
        });

        Assert.Equal(ConsoleColor.DarkBlue, driver.GetCell(3, 2).Background);
        Assert.Equal(ConsoleColor.Black, driver.GetCell(2, 2).Background);
        Assert.Equal('─', driver.GetCell(3, 1).Character);
        Assert.Equal('─', driver.GetCell(4, 1).Character);
    }

    [Fact]
    public void BufferedFrame_WideTextUsesCellWidthAndDoesNotRemainDirty()
    {
        var (renderer, driver) = Create(4, 1);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "界AB", style);

        Assert.Contains(driver.WriteRecords, write => write.X == 0 && write.Text == "界AB");
        driver.ClearRecordedOperations();

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "界AB", style);

        Assert.Equal(0, driver.WriteAtCallCount);
    }

    [Fact]
    public void FillRegion_FillsWithSpacesInStyle()
    {
        var (renderer, driver) = Create(20, 10);
        driver.WriteAt(0, 2, "XXXXXXXXXX".AsSpan());

        var style = new CellStyle(ConsoleColor.Gray, ConsoleColor.DarkBlue);
        renderer.FillRegion(new Rect(0, 2, 10, 1), style);

        for (int x = 0; x < 10; x++)
        {
            Assert.Equal(' ', driver.GetCell(x, 2).Character);
            Assert.Equal(ConsoleColor.DarkBlue, driver.GetCell(x, 2).Background);
        }
    }

    [Fact]
    public void BufferedFrame_RepeatedIdenticalFrame_DoesNotWriteAgain()
    {
        var (renderer, driver) = Create(20, 5);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "ABC", style);

        Assert.True(driver.WriteAtCallCount > 0);
        driver.ClearRecordedOperations();

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "ABC", style);

        Assert.Equal(0, driver.WriteAtCallCount);
    }

    [Fact]
    public void ClearScreen_SynchronizesBufferedState()
    {
        var (renderer, driver) = Create(20, 5);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "ABC", style);

        renderer.ClearScreen();
        driver.ClearRecordedOperations();

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "ABC", style);

        Assert.True(driver.WriteAtCallCount > 0);
        Assert.StartsWith("ABC", driver.GetRow(0));
    }

    [Fact]
    public void TryScrollViewportToBottom_MovesViewportAndForcesNextFrameWrites()
    {
        var (renderer, driver) = Create(20, 5);
        driver.SetBufferHeight(20);
        driver.SetViewportOrigin(0, 3);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "ABC", style);

        driver.ClearRecordedOperations();
        Assert.True(renderer.TryScrollViewportToBottom());
        Assert.Equal(15, driver.GetViewport().Top);

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "ABC", style);

        Assert.True(driver.WriteAtCallCount > 0);
    }

    [Fact]
    public void BufferedFrame_CursorStyleMove_WritesOnlyChangedRows()
    {
        var (renderer, driver) = Create(20, 5);
        var normal = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);
        var cursor = new CellStyle(ConsoleColor.Black, ConsoleColor.Cyan);
        var region = new Rect(0, 0, 20, 5);

        using (renderer.BeginFrame())
        {
            renderer.FillRegion(region, normal);
            renderer.Write(0, 1, "alpha", normal);
            renderer.Write(0, 2, "beta", cursor);
        }

        driver.ClearRecordedOperations();

        using (renderer.BeginFrame())
        {
            renderer.FillRegion(region, normal);
            renderer.Write(0, 1, "alpha", cursor);
            renderer.Write(0, 2, "beta", normal);
        }

        Assert.Equal(2, driver.WriteAtCallCount);
        Assert.Contains(driver.WriteRecords, r => r.Y == 1 && r.Text == "alpha");
        Assert.Contains(driver.WriteRecords, r => r.Y == 2 && r.Text == "beta");
    }

    [Fact]
    public void Capture_SynchronizesBufferedState()
    {
        var (renderer, driver) = Create(10, 5);
        var style = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        driver.WriteAt(0, 0, "REAL".AsSpan(), style.Foreground, style.Background);
        driver.ClearRecordedOperations();

        renderer.Capture(new Rect(0, 0, 10, 5));

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "REAL", style);

        Assert.Equal(0, driver.WriteAtCallCount);
    }

    [Fact]
    public void CapturedExternalFrame_Win32FrameBatch_WritesOnlyDirtyCells()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            Capabilities = ConsoleFrameWriteCapabilities.WindowsCells,
        };
        var renderer = new ScreenRenderer(driver, ScreenPresentationMode.Win32FrameBatch);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.Black);
        driver.WriteAt(0, 0, "shell history".AsSpan(), style.Foreground, style.Background);
        driver.ClearRecordedOperations();

        using (renderer.BeginFrameFromCurrentViewportCapture())
            renderer.Write(4, 24, "prompt", style);

        Assert.Empty(driver.CellWriteRecords);
        Assert.Contains(driver.WriteRecords, write => write.X == 4 && write.Y == 24 && write.Text == "prompt");
        Assert.StartsWith("shell history", driver.GetRow(0));
    }

    [Fact]
    public void ApplicationFrame_Win32FrameBatch_KeepsFullViewportWrite()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            Capabilities = ConsoleFrameWriteCapabilities.WindowsCells,
        };
        var renderer = new ScreenRenderer(driver, ScreenPresentationMode.Win32FrameBatch);

        using (renderer.BeginFrame())
            renderer.FillRegion(new Rect(0, 0, 80, 25), CellStyle.Default);

        Assert.Equal(
            new FakeConsoleDriver.CellWriteRecord(0, 0, 80, 25),
            Assert.Single(driver.CellWriteRecords));
    }

    [Fact]
    public void InterruptedCapturedFrame_DoesNotLeakExternalOwnershipToNextFrame()
    {
        var driver = new FakeConsoleDriver(20, 5)
        {
            Capabilities = ConsoleFrameWriteCapabilities.WindowsCells,
        };
        var renderer = new ScreenRenderer(driver, ScreenPresentationMode.Win32FrameBatch);
        driver.BeforeViewportWrite = current =>
        {
            current.BeforeViewportWrite = null;
            current.SetSize(21, 5);
        };

        using (renderer.BeginFrameFromCurrentViewportCapture())
            renderer.Write(0, 4, "prompt", CellStyle.Default);

        Assert.True(renderer.FrameWasInterrupted);
        driver.ClearRecordedOperations();

        using (renderer.BeginFrame())
            renderer.FillRegion(new Rect(0, 0, 21, 5), CellStyle.Default);

        Assert.Equal(
            new FakeConsoleDriver.CellWriteRecord(0, 0, 21, 5),
            Assert.Single(driver.CellWriteRecords));
    }

    [Fact]
    public void InvalidatePhysicalOutput_RedrawsCapturedContentAfterSurfaceChanges()
    {
        var (renderer, driver) = Create(10, 5);
        var style = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        driver.WriteAt(0, 0, "PROMPT".AsSpan(), style.Foreground, style.Background);
        renderer.Capture(new Rect(0, 0, 10, 5));

        renderer.InvalidatePhysicalOutput();
        driver.ClearRegion(new Rect(0, 0, 10, 5));
        driver.ClearRecordedOperations();

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "PROMPT", style);

        Assert.StartsWith("PROMPT", driver.GetRow(0));
        Assert.NotEqual(0, driver.WriteAtCallCount);
    }

    [Fact]
    public void Restore_SynchronizesBufferedState()
    {
        var (renderer, driver) = Create(10, 5);
        var region = new Rect(0, 0, 10, 5);
        var cells = new SnapshotCell[5, 10];
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 10; x++)
                cells[y, x] = new SnapshotCell { Character = ' ', Foreground = ConsoleColor.Gray, Background = ConsoleColor.Black };

        var style = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        cells[1, 0] = new SnapshotCell { Character = 'S', Foreground = style.Foreground, Background = style.Background };
        cells[1, 1] = new SnapshotCell { Character = 'A', Foreground = style.Foreground, Background = style.Background };
        cells[1, 2] = new SnapshotCell { Character = 'V', Foreground = style.Foreground, Background = style.Background };
        cells[1, 3] = new SnapshotCell { Character = 'E', Foreground = style.Foreground, Background = style.Background };

        renderer.Restore(new ScreenSnapshot(driver.GetViewport(), region, cells));
        driver.ClearRecordedOperations();

        using (renderer.BeginFrame())
            renderer.Write(0, 1, "SAVE", style);

        Assert.Equal(0, driver.WriteAtCallCount);
    }

    [Fact]
    public void BufferedFrame_SizeChange_ForcesNextFrameWrites()
    {
        var (renderer, driver) = Create(10, 5);

        using (renderer.BeginFrame())
            renderer.FillRegion(new Rect(0, 0, 10, 5), CellStyle.Default);

        driver.ClearRecordedOperations();
        driver.SetSize(12, 5);

        using (renderer.BeginFrame())
            renderer.FillRegion(new Rect(0, 0, 12, 5), CellStyle.Default);

        Assert.True(driver.WriteAtCallCount > 0);
    }

    [Fact]
    public void BufferedFrame_SizeChangeDuringFrame_InterruptsFlushAndLeavesDriverUnchanged()
    {
        var (renderer, driver) = Create(10, 5);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        using (renderer.BeginFrame())
        {
            renderer.Write(0, 0, "OLD", style);
            driver.SetSize(12, 5);
            renderer.Write(10, 0, "X", style);
        }

        // FlushFrame detects the size mismatch and aborts — nothing is written to the driver.
        Assert.True(renderer.FrameWasInterrupted);
        Assert.Equal(' ', driver.GetCell(0, 0).Character);
    }

    [Fact]
    public void BufferedFrame_ViewportOriginChange_ForcesFullRedraw()
    {
        var (renderer, driver) = Create(10, 5);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "ABC", style);

        driver.ClearRecordedOperations();
        driver.SetViewportOrigin(0, 1);

        using (renderer.BeginFrame())
            renderer.Write(0, 0, "ABC", style);

        Assert.False(renderer.FrameWasInterrupted);
        Assert.True(driver.WriteAtCallCount > 0);
    }

    [Fact]
    public void BufferedFrame_ViewportOriginChangeDuringFlush_DoesNotApplyPendingCursor()
    {
        var (renderer, driver) = Create(10, 5);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        using (renderer.BeginFrame())
        {
            renderer.Write(0, 0, "ABC", style);
            renderer.SetCursorPosition(2, 1);
            driver.SetViewportOrigin(0, 1);
        }

        Assert.True(renderer.FrameWasInterrupted);
        Assert.Equal(0, driver.TrySetCursorPositionInViewportCallCount);
    }

    [Fact]
    public void BufferedFrame_CursorUsesFrameViewportOrigin()
    {
        var (renderer, driver) = Create(10, 5);
        driver.SetViewportOrigin(5, 20);

        using (renderer.BeginFrame())
            renderer.SetCursorPosition(2, 3);

        Assert.False(renderer.FrameWasInterrupted);
        Assert.Equal(7, driver.CursorX);
        Assert.Equal(23, driver.CursorY);
    }

    [Fact]
    public void ReadKey_UsesPendingKeyPreservedByDrainResizeEvents()
    {
        var (renderer, driver) = Create(10, 5);
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

        renderer.DrainResizeEvents();

        var key = renderer.ReadKey();

        Assert.Equal(ConsoleKey.Enter, key.Key);
    }

    [Fact]
    public void SetCursorVisible_ForwardsRepeatedHide()
    {
        var (renderer, driver) = Create();

        renderer.SetCursorVisible(false);
        renderer.SetCursorVisible(false);
        renderer.SetCursorVisible(true);
        renderer.SetCursorVisible(true);

        Assert.Equal(3, driver.SetCursorVisibleCallCount);
    }

    [Fact]
    public void SetRenderingOutputMode_DelegatesToDriver()
    {
        var (renderer, driver) = Create();

        renderer.SetRenderingOutputMode(true);
        Assert.True(driver.RenderingOutputMode);

        renderer.SetRenderingOutputMode(false);
        Assert.False(driver.RenderingOutputMode);
    }

    [Fact]
    public void SetConsoleScrollbackEnabled_DelegatesToDriver()
    {
        var (renderer, driver) = Create();

        renderer.SetConsoleScrollbackEnabled(false);
        Assert.False(driver.ConsoleScrollbackEnabled);

        renderer.SetConsoleScrollbackEnabled(true);
        Assert.True(driver.ConsoleScrollbackEnabled);
    }

    [Fact]
    public void EnterChildProcessConsoleMode_DelegatesScopedModeToDriver()
    {
        var (renderer, driver) = Create();

        using (renderer.EnterChildProcessConsoleMode())
        {
            Assert.True(driver.ChildProcessConsoleMode);
        }

        Assert.False(driver.ChildProcessConsoleMode);
        Assert.Equal(1, driver.EnterChildProcessConsoleModeCallCount);
        Assert.Equal(1, driver.RestoreApplicationInputModeCallCount);
    }

    [Fact]
    public void DrawBox_RendersCorrectBorderCharacters()
    {
        var (renderer, driver) = Create(20, 10);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);

        renderer.DrawBox(new Rect(0, 0, 10, 5), style);

        // Corners
        Assert.Equal('┌', driver.GetCell(0, 0).Character);
        Assert.Equal('┐', driver.GetCell(9, 0).Character);
        Assert.Equal('└', driver.GetCell(0, 4).Character);
        Assert.Equal('┘', driver.GetCell(9, 4).Character);

        // Top edge
        Assert.Equal('─', driver.GetCell(1, 0).Character);
        Assert.Equal('─', driver.GetCell(8, 0).Character);

        // Left/right edges
        Assert.Equal('│', driver.GetCell(0, 1).Character);
        Assert.Equal('│', driver.GetCell(9, 1).Character);
    }

    [Fact]
    public void DrawDoubleBox_RendersFarLikeBorderCharacters()
    {
        var (renderer, driver) = Create(20, 10);
        var style = new CellStyle(ConsoleColor.Cyan, ConsoleColor.DarkBlue);

        renderer.DrawDoubleBox(new Rect(0, 0, 10, 5), style);

        Assert.Equal('╔', driver.GetCell(0, 0).Character);
        Assert.Equal('╗', driver.GetCell(9, 0).Character);
        Assert.Equal('╚', driver.GetCell(0, 4).Character);
        Assert.Equal('╝', driver.GetCell(9, 4).Character);
        Assert.Equal('═', driver.GetCell(1, 0).Character);
        Assert.Equal('║', driver.GetCell(0, 1).Character);
    }

    [Fact]
    public void CaptureAndRestore_PreservesScreenState()
    {
        var (renderer, driver) = Create(20, 10);
        var style = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkRed);
        renderer.Write(0, 0, "ORIGINAL  ", style);

        var snapshot = renderer.Capture(new Rect(0, 0, 10, 1));
        renderer.Write(0, 0, "OVERWRITE ", CellStyle.Default);

        renderer.Restore(snapshot);

        Assert.Equal('O', driver.GetCell(0, 0).Character);
        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(0, 0).Foreground);
    }

}
