using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class CommandLineRendererTests
{
    [Fact]
    public void Render_LongCommandAtEnd_ShowsBlankCellAfterLastCharacter()
    {
        var driver = new FakeConsoleDriver(width: 10, height: 1);
        var renderer = new CommandLineRenderer(new ScreenRenderer(driver));
        var state = new CommandLineState();
        state.SetText("abcdefghij");

        renderer.Render(0, 10, string.Empty, state);

        Assert.Equal("bcdefghij ", driver.GetRow(0));
        Assert.Equal(9, renderer.GetCursorX(10, string.Empty, state));
        Assert.Equal('j', driver.GetCell(8, 0).Character);
        Assert.Equal(' ', driver.GetCell(9, 0).Character);
    }

    [Fact]
    public void Render_LongCommandBeforeLastCharacter_DoesNotShowTrailingBlankCell()
    {
        var driver = new FakeConsoleDriver(width: 10, height: 1);
        var renderer = new CommandLineRenderer(new ScreenRenderer(driver));
        var state = new CommandLineState();
        state.SetText("abcdefghij");
        state.MoveCursor(-1);

        renderer.Render(0, 10, string.Empty, state);

        Assert.Equal("abcdefghij", driver.GetRow(0));
        Assert.Equal(9, renderer.GetCursorX(10, string.Empty, state));
        Assert.Equal('j', driver.GetCell(9, 0).Character);
    }

    [Fact]
    public void Render_SelectionUsesInvertedCommandLineStyle()
    {
        var driver = new FakeConsoleDriver(width: 40, height: 3);
        var renderer = new CommandLineRenderer(new ScreenRenderer(driver));
        var state = new CommandLineState();
        state.SetText("abc");
        state.SelectAll();

        renderer.Render(1, 40, "C:\\Work", state);

        int promptLength = "C:\\Work>".Length;
        var promptCell = driver.GetCell(0, 1);
        var selectedCell = driver.GetCell(promptLength, 1);

        Assert.Equal('C', promptCell.Character);
        Assert.Equal(ConsoleColor.White, promptCell.Foreground);
        Assert.Equal(ConsoleColor.Black, promptCell.Background);
        Assert.Equal('a', selectedCell.Character);
        Assert.Equal(ConsoleColor.Black, selectedCell.Foreground);
        Assert.Equal(ConsoleColor.White, selectedCell.Background);
    }
}
