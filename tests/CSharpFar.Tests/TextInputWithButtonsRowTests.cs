using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TextInputWithButtonsRowTests
{
    [Fact]
    public void MouseClickButton_ReturnsPrefixedCommand()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 5);
        var screen = new ScreenRenderer(driver);
        var text = new CommandLineState();
        text.SetText("14.06.2026 15:03:39");
        var row = new TextInputWithButtonsRow(
            "write:    ",
            text,
            [
                new DialogButton("original", "Original", 'O'),
                new DialogButton("current", "Current", 'U'),
                new DialogButton("blank", "Blank", 'B'),
            ],
            "write.",
            inputWidth: 19,
            buttonAreaWidth: 36);

        row.Render(new FormRowRenderContext(screen, new Rect(0, 0, 80, 1), focused: true));
        FormInputResult result = row.HandleMouse(
            new MouseConsoleInputEvent(45, 0, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            new FormRowMouseContext(new Rect(0, 0, 80, 1), rowIndex: 0, focused: true, screenHeight: 5));

        Assert.Equal(FormInputResultKind.Submit, result.Kind);
        Assert.Equal("write.current", result.Command);
    }
}
