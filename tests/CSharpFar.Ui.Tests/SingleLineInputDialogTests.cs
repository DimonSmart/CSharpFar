using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class SingleLineInputDialogTests
{
    [Fact]
    public void Show_DuplicateNameDisplaysTwoRedRowsAndButtonsBelowThem()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        string? result = new SingleLineInputDialog(
            ModalTestHost.Create(driver),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(new SingleLineInputDialogOptions
            {
                Title = "Make folder",
                Prompt = "Create the folder:",
                InitialText = "existing-directory",
                PreferredWidth = 64,
                PreferredHeight = 11,
                ErrorLines = (name, _) => [name, "already exists."],
                Validate = _ => "Folder already exists.",
            });

        Assert.Null(result);
        FakeConsoleDriver.WriteRecord name = driver.WriteRecords.First(write =>
            write.Foreground == ConsoleColor.Red && write.Text.Contains("existing-directory", StringComparison.Ordinal));
        FakeConsoleDriver.WriteRecord message = driver.WriteRecords.First(write =>
            write.Foreground == ConsoleColor.Red && write.Text.Contains("already exists.", StringComparison.Ordinal));
        Assert.Equal(name.Y + 1, message.Y);
        Assert.Contains(driver.WriteRecords, write =>
            write.Text.Contains("{ OK }", StringComparison.Ordinal) && write.Y > message.Y);
    }

    [Fact]
    public void Show_LongDuplicateNameFitsInErrorRow()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        string longName = new string('x', 140);

        string? result = new SingleLineInputDialog(
            ModalTestHost.Create(driver),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(new SingleLineInputDialogOptions
            {
                Title = "Make folder",
                Prompt = "Create the folder:",
                InitialText = longName,
                PreferredWidth = 64,
                PreferredHeight = 11,
                ErrorLines = (name, _) => [name, "already exists."],
                Validate = _ => "Folder already exists.",
            });

        Assert.Null(result);
        Assert.Contains(driver.WriteRecords, write =>
            write.Foreground == ConsoleColor.Red && write.Text.Contains("already exists.", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, write =>
            write.Foreground == ConsoleColor.Red && write.Text.Length > 64);
    }

    [Fact]
    public void Show_MouseClickOkConfirmsInitialText()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.BeforeReadInput = current =>
        {
            current.BeforeReadInput = null;
            FakeConsoleDriver.WriteRecord button = current.WriteRecords.Last(write =>
                write.Text.Contains("{ OK }", StringComparison.Ordinal));
            int offset = button.Text.IndexOf("{ OK }", StringComparison.Ordinal);
            int x = button.X + offset + 2;

            current.EnqueueInput(new MouseConsoleInputEvent(
                x, button.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            current.EnqueueInput(new MouseConsoleInputEvent(
                x, button.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
        };

        string? result = new SingleLineInputDialog(
            ModalTestHost.Create(driver),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(new SingleLineInputDialogOptions
            {
                Title = "Input",
                Prompt = "Name",
                InitialText = "value",
            });

        Assert.Equal("value", result);
    }
}
