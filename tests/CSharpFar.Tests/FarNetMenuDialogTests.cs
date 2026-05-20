using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class FarNetMenuDialogTests
{
    [Fact]
    public void Show_LeftMouseClickOnItemSelectsIt()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        driver.EnqueueInput(new MouseConsoleInputEvent(
            X: 36,
            Y: 12,
            Button: MouseButton.Left,
            Kind: MouseEventKind.Down,
            Modifiers: MouseKeyModifiers.None));

        int? selected = new FarNetMenuDialog(new ScreenRenderer(driver)).Show(
            "Menu",
            ["One", "Two", "Three"],
            selected: 0);

        Assert.Equal(1, selected);
    }

    [Fact]
    public void Show_MouseWheelMovesSelection()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        driver.EnqueueInput(new MouseConsoleInputEvent(
            X: 36,
            Y: 12,
            Button: MouseButton.WheelDown,
            Kind: MouseEventKind.Wheel,
            Modifiers: MouseKeyModifiers.None));
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

        int? selected = new FarNetMenuDialog(new ScreenRenderer(driver)).Show(
            "Menu",
            ["One", "Two", "Three"],
            selected: 0);

        Assert.Equal(1, selected);
    }
}
