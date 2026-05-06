using CSharpFar.Console;
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
