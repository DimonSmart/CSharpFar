using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ModuleHelpDialogTests
{
    [Fact]
    public void Show_DragScrollbarOutsideBoundsThenCloses()
    {
        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.EnqueueInput(new MouseConsoleInputEvent(39, 2, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueInput(new MouseConsoleInputEvent(0, 6, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None));
        driver.EnqueueInput(new MouseConsoleInputEvent(0, 6, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, false, false, false));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));

        new ModuleHelpDialog(new ModalDialogHost(composition)).Show(
            "Help",
            Enumerable.Range(1, 12).Select(index => $"line {index}").ToArray());

        string rendered = string.Join('\n', driver.WriteRecords.Select(record => record.Text));
        Assert.Contains("line 12", rendered, StringComparison.Ordinal);
        Assert.False(driver.CursorVisible);
    }
}
