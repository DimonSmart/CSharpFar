using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class MessageDialogTests
{
    [Fact]
    public void Show_WrapsLongErrorInsteadOfTruncating()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 20);
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        string message =
            "Open from clipboard expects JSON array or object or a file path like \"*.json\".\r\n" +
            "Error: Plugin command 'open-from-clipboard' failed while parsing the input payload.";

        Show(driver, "Module", message);

        string rendered = string.Join('\n', driver.WriteRecords.Select(record => record.Text));
        Assert.Contains("Open from clipboard expects", rendered, StringComparison.Ordinal);
        Assert.Contains("Plugin command", rendered, StringComparison.Ordinal);
        Assert.Contains("input payload.", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2026failed", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_ScrollsLongMessage()
    {
        var driver = new FakeConsoleDriver(width: 60, height: 8);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        string message = string.Join('\n', ["line 1", "line 2", "line 3", "line 4", "line 5"]);

        Show(driver, "Module", message);

        string rendered = string.Join('\n', driver.WriteRecords.Select(record => record.Text));
        Assert.Contains("line 4", rendered, StringComparison.Ordinal);
        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void Show_WheelOverTextScrollsLongMessage()
    {
        var driver = new FakeConsoleDriver(width: 60, height: 8);
        int inputCount = 0;
        driver.BeforeReadInput = current =>
        {
            if (inputCount++ == 0)
            {
                var line = current.WriteRecords.Last(record => record.Text.Contains("line 1", StringComparison.Ordinal));
                current.EnqueueInput(new MouseConsoleInputEvent(line.X, line.Y, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None));
            }
            else
            {
                current.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            }
        };

        Show(driver, "Module", string.Join('\n', ["line 1", "line 2", "line 3", "line 4", "line 5", "line 6"]));

        string rendered = string.Join('\n', driver.WriteRecords.Select(record => record.Text));
        Assert.Contains("line 4", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowButtons_RightThenEnterSelectsSecondButton()
    {
        var driver = new FakeConsoleDriver(width: 60, height: 10);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

        int result = CreateDialog(driver).ShowButtons("Question", "Choose", ["First", "Second"]);

        Assert.Equal(1, result);
        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void ShowButtons_ScrollsMessageThenActivatesSelectedButton()
    {
        var driver = new FakeConsoleDriver(width: 60, height: 8);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        int result = CreateDialog(driver).ShowButtons(
            "Question",
            string.Join('\n', ["line 1", "line 2", "line 3", "line 4", "line 5", "line 6"]),
            ["First", "Second"]);

        string rendered = string.Join('\n', driver.WriteRecords.Select(record => record.Text));
        Assert.Contains("line 4", rendered, StringComparison.Ordinal);
        Assert.Equal(1, result);
    }

    [Fact]
    public void ShowButtons_MouseSelectsButton()
    {
        var driver = new FakeConsoleDriver(width: 60, height: 10);
        driver.BeforeReadInput = current =>
        {
            var record = current.WriteRecords.Last(value => value.Text.Contains("Second", StringComparison.Ordinal));
            current.EnqueueInput(new MouseConsoleInputEvent(
                record.X + record.Text.IndexOf("Second", StringComparison.Ordinal),
                record.Y,
                MouseButton.Left,
                MouseEventKind.Down,
                MouseKeyModifiers.None));
        };

        int result = CreateDialog(driver).ShowButtons("Question", "Choose", ["First", "Second"]);

        Assert.Equal(1, result);
    }

    private static void Show(FakeConsoleDriver driver, string title, string message)
    {
        CreateDialog(driver).Show(title, message);
    }

    private static MessageDialog CreateDialog(FakeConsoleDriver driver)
    {
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        return new MessageDialog(new ModalDialogHost(composition));
    }
}
