using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class CommandLineRendererTests
{
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
