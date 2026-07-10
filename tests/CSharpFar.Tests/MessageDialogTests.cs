using CSharpFar.Console;
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

        Assert.False(driver.CursorVisible);
    }

    private static void Show(FakeConsoleDriver driver, string title, string message)
    {
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        new MessageDialog(new ModalDialogHost(composition)).Show(title, message);
    }
}
