using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class SearchOptionsDialogTests
{
    [Fact]
    public void Show_EnterConfirmsInitialPattern()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));

        var result = ShowDialog(driver, initialPattern: "abc");

        Assert.NotNull(result);
        Assert.Equal("abc", result.Pattern);
    }

    [Fact]
    public void Show_EscapeCancels()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

        var result = ShowDialog(driver, initialPattern: "abc");

        Assert.Null(result);
    }

    [Fact]
    public void Show_MouseClickButtonConfirms()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueInput(new MouseConsoleInputEvent(35, 14, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        var result = ShowDialog(driver, initialPattern: "abc");

        Assert.NotNull(result);
        Assert.Equal("abc", result.Pattern);
    }

    [Fact]
    public void Show_MouseClickCheckboxTogglesOption()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueInput(new MouseConsoleInputEvent(15, 11, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueInput(new MouseConsoleInputEvent(35, 14, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        var result = ShowDialog(driver, initialPattern: "abc");

        Assert.NotNull(result);
        Assert.True(result.GetOption("case-sensitive"));
    }

    private static SearchOptionsDialogResult? ShowDialog(FakeConsoleDriver driver, string initialPattern) =>
        new SearchOptionsDialog(new ScreenRenderer(driver)).Show(new SearchOptionsDialogOptions
        {
            InitialPattern = initialPattern,
            HistoryKey = $"SearchOptionsDialogTests:{Guid.NewGuid()}",
            Width = 56,
            Options =
            [
                new SearchOptionLine("case-sensitive", "Case sensitive", false),
                new SearchOptionLine("whole-words", "Whole words", false),
            ],
        });
}
