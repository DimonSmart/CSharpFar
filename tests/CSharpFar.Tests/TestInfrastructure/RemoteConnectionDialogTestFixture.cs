using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;
using CSharpFar.Testing;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

/// <summary>Shared test harness for the FTP/SFTP connection dialogs.</summary>
internal static class RemoteConnectionDialogTestFixture
{
    public static FakeConsoleDriver CreateDriver(int height = 30, int width = 100) =>
        new(width: width, height: height);

    public static SftpConnectionDialog CreateSftpDialog(ModalDialogHost modalDialogs, FormFieldFactory fields) =>
        new(new DialogService(modalDialogs, fields), fields);

    public static FtpConnectionDialog CreateFtpDialog(
        ModalDialogHost modalDialogs, ITextFieldHistoryProvider? history = null) =>
        new(
            new DialogService(modalDialogs, new FormFieldFactory(history ?? TextFieldHistoryTestProvider.Create())),
            new FormFieldFactory(history ?? TextFieldHistoryTestProvider.Create()));

    public static DialogService CreateDialogs(ModalDialogHost modalDialogs) =>
        new(modalDialogs, new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

    public static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    public static ConsoleKeyInfo ShiftTab() =>
        new('\0', ConsoleKey.Tab, shift: true, alt: false, control: false);

    public static ConsoleKeyInfo Printable(char character) =>
        new(character, ConsoleKey.NoName, shift: false, alt: false, control: false);

    public static ConsoleKeyInfo ControlA() =>
        new('\u0001', ConsoleKey.A, shift: false, alt: false, control: true);

    public static ConsoleKeyInfo CharKey(char key) =>
        new(key, Enum.Parse<ConsoleKey>(key.ToString(), ignoreCase: true), shift: false, alt: false, control: false);

    public static MouseConsoleInputEvent LeftMouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    public static bool IsCursorInsideInputForLabel(
        FakeConsoleDriver driver, string label, out string diagnostic)
    {
        FakeConsoleDriver.WriteRecord record = LastRenderedText(driver, label);
        diagnostic = $"cursor=({driver.CursorX},{driver.CursorY}) visible={driver.CursorVisible}; label=({record.X},{record.Y})";
        return driver.CursorVisible && driver.CursorY == record.Y;
    }

    public static bool IsCursorOnRowText(FakeConsoleDriver driver, string text)
    {
        var record = LastRenderedText(driver, text);
        return driver.CursorVisible && driver.CursorY == record.Y;
    }

    public static FakeConsoleDriver.WriteRecord LastRenderedText(FakeConsoleDriver driver, string text) =>
        driver.WriteRecords.Last(record => record.Text.Contains(text, StringComparison.Ordinal));

    public static bool HasRenderedText(FakeConsoleDriver driver, string text) =>
        driver.WriteRecords.Any(record => record.Text.Contains(text, StringComparison.Ordinal));

    public static void ClickInputByLabel(FakeConsoleDriver driver, string label)
    {
        var record = LastRenderedText(driver, label);
        driver.EnqueueInput(LeftMouse(record.X + 22, record.Y));
    }

    public static void ClickRowByText(FakeConsoleDriver driver, string text)
    {
        var record = LastRenderedText(driver, text);
        driver.EnqueueInput(LeftMouse(record.X + Math.Min(1, Math.Max(0, record.Text.Length - 1)), record.Y));
    }

    public static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char character in text)
            driver.EnqueueKey(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
    }

    public static void RunReadScript(FakeConsoleDriver driver, params Action<FakeConsoleDriver>[] steps)
    {
        int index = 0;
        Action<FakeConsoleDriver>? callback = null;
        callback = currentDriver =>
        {
            if (index >= steps.Length)
                return;
            steps[index++](currentDriver);
            if (index < steps.Length)
                currentDriver.BeforeReadInput = callback;
        };
        driver.BeforeReadInput = callback;
    }
}

/// <summary>Lazy temporary workspace shared by FTP and SFTP test suites.</summary>
internal sealed class RemoteConnectionTestWorkspace(string prefix) : IDisposable
{
    public string DirectoryPath { get; } =
        Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
            Directory.Delete(DirectoryPath, recursive: true);
    }
}
