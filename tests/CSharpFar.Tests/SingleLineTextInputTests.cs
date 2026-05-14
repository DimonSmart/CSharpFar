using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public class SingleLineTextInputTests
{
    [Fact]
    public void HandleKey_ControlASelectsAllAndNextTypingReplacesSelection()
    {
        var buffer = new CommandLineState();
        buffer.SetText("sample");
        string? error = "old error";

        var result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true),
            ref error);

        Assert.Equal(TextInputKeyResult.Handled, result);
        Assert.True(buffer.HasSelection);
        Assert.Equal(0, buffer.SelectionStart);
        Assert.Equal(6, buffer.SelectionLength);
        Assert.Equal("old error", error);

        result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('x', ConsoleKey.X, shift: false, alt: false, control: false),
            ref error);

        Assert.Equal(TextInputKeyResult.TextChanged, result);
        Assert.Equal("x", buffer.Text);
        Assert.False(buffer.HasSelection);
        Assert.Null(error);
    }

    [Fact]
    public void HandleKey_ControlAAlsoAcceptsControlCharacter()
    {
        var buffer = new CommandLineState();
        buffer.SetText("sample");
        string? error = null;

        var result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: false),
            ref error);

        Assert.Equal(TextInputKeyResult.Handled, result);
        Assert.True(buffer.HasSelection);
    }

    [Fact]
    public void Render_UsesSelectionStyleForVisibleSelectedText()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 2);
        var screen = new ScreenRenderer(driver);
        var buffer = new CommandLineState();
        buffer.SetText("abcdef");
        buffer.SelectAll();

        var normal = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        var selected = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Blue);

        SingleLineTextInput.Render(screen, 1, 0, 7, buffer, normal, selected);

        Assert.Equal('a', driver.GetCell(1, 0).Character);
        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(1, 0).Foreground);
        Assert.Equal(ConsoleColor.Blue, driver.GetCell(1, 0).Background);
        Assert.Equal(ConsoleColor.Blue, driver.GetCell(6, 0).Background);
        Assert.Equal(ConsoleColor.Black, driver.GetCell(7, 0).Background);
    }
}
