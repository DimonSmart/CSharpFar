using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;

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
    public void SetCursorVisible_IgnoresRepeatedState()
    {
        var (renderer, driver) = Create();

        renderer.SetCursorVisible(false);
        renderer.SetCursorVisible(false);
        renderer.SetCursorVisible(true);
        renderer.SetCursorVisible(true);

        Assert.Equal(2, driver.SetCursorVisibleCallCount);
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

    [Fact]
    public void GetSize_ReturnsDriverSize()
    {
        var (renderer, _) = Create(132, 50);
        var size = renderer.GetSize();

        Assert.Equal(132, size.Width);
        Assert.Equal(50, size.Height);
    }
}
