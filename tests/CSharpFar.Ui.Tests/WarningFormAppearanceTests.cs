namespace CSharpFar.Ui.Tests;

public sealed class WarningFormAppearanceTests
{
    [Fact]
    public void WarningForm_AppliesWarningPaletteToBodyAndInheritedButtons()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        ButtonRow actions = FormControls.Buttons(
            DialogButton.Default("ok", "OK", 'O'),
            DialogButton.Cancel());

        _ = new FormDialogs(ModalTestHost.Create(driver)).Show(
            new FormDialogOptions("Warning", 40, 10)
            {
                Appearance = DialogAppearance.Warning,
                InitialFocus = actions,
            },
            rows: () =>
            [
                FormControls.Label("Warning body"),
                FormControls.Separator(),
                FormControls.CheckBox("Remember choice"),
            ],
            footer: () => [actions],
            handle: formEvent => formEvent.IsCancelled
                ? FormDialogOutcome<bool>.Complete(false)
                : FormDialogOutcome<bool>.Continue());

        Assert.Equal(
            UiTheme.Current.WarningBackground,
            driver.WriteRecords.Last(record => record.Text.Contains("Warning body", StringComparison.Ordinal)).Background);
        Assert.Equal(
            UiTheme.Current.WarningBackground,
            driver.WriteRecords.Last(record => record.Text.Contains("Remember choice", StringComparison.Ordinal)).Background);
        Assert.Equal(
            UiTheme.Current.WarningBackground,
            driver.WriteRecords.Last(record => record.Text.Contains("[ Cancel ]", StringComparison.Ordinal)).Background);
        Assert.Equal(
            UiTheme.Current.WarningButtonFocusedBackground,
            driver.WriteRecords.Last(record => record.Text.Contains("{ OK }", StringComparison.Ordinal)).Background);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}

