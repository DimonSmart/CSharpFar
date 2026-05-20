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
            "Error: FarNet API 'AnyEditor' is not supported by CSharpFar FarNet compatibility v1.";

        new MessageDialog(new ScreenRenderer(driver)).Show("Module", message);

        string rendered = string.Join('\n', driver.WriteRecords.Select(record => record.Text));
        Assert.Contains("Open from clipboard expects", rendered, StringComparison.Ordinal);
        Assert.Contains("FarNet API 'AnyEditor'", rendered, StringComparison.Ordinal);
        Assert.Contains("compatibility v1.", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2026supported", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_ScrollsLongMessage()
    {
        var driver = new FakeConsoleDriver(width: 60, height: 8);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        string message = string.Join('\n', ["line 1", "line 2", "line 3", "line 4", "line 5"]);

        new MessageDialog(new ScreenRenderer(driver)).Show("Module", message);

        string rendered = string.Join('\n', driver.WriteRecords.Select(record => record.Text));
        Assert.Contains("line 3", rendered, StringComparison.Ordinal);
        Assert.Contains("line 4", rendered, StringComparison.Ordinal);
    }
}
