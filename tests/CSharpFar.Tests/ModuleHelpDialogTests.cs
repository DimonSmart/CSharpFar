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
        string? draggedScreen = null;
        int readCount = 0;
        Action<FakeConsoleDriver>? observeRead = null;
        observeRead = current =>
        {
            readCount++;
            if (readCount == 3)
            {
                draggedScreen = string.Join(
                    '\n',
                    Enumerable.Range(0, current.GetSize().Height).Select(current.GetRow));
            }
            else
            {
                current.BeforeReadInput = observeRead;
            }
        };
        driver.BeforeReadInput = observeRead;
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));

        new ModuleHelpDialog(new ModalDialogHost(composition)).Show(
            "Help",
            Enumerable.Range(1, 12).Select(index => $"line {index}").ToArray());

        Assert.Contains("line 12", Assert.IsType<string>(draggedScreen), StringComparison.Ordinal);
        Assert.False(driver.CursorVisible);
    }
}
