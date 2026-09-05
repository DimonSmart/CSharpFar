using CSharpFar.App.Dialogs;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class WarningFormAppearanceTests
{
    [Fact]
    public void ConflictDialog_InheritsWarningAppearanceFromForm()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        FileOperationConflictDecision decision = new ConflictDialog(
            new DialogService(
                ModalTestHost.Create(driver),
                new FormFieldFactory(TextFieldHistoryTestProvider.Create())))
            .Show(new FileOperationConflict
            {
                SourcePath = @"C:\src\a.txt",
                DestinationPath = @"C:\dst\a.txt",
                SourceSize = 3,
                DestinationSize = 5,
            });

        Assert.Equal(ConflictDecisionMode.Cancel, decision.Mode);
        Assert.Equal(
            UiTheme.Current.WarningBackground,
            driver.WriteRecords.Last(record => record.Text.Contains("File already exists", StringComparison.Ordinal)).Background);
        Assert.Equal(
            UiTheme.Current.WarningBackground,
            driver.WriteRecords.Last(record => record.Text.Contains("Remember choice", StringComparison.Ordinal)).Background);
        Assert.Equal(
            UiTheme.Current.WarningBackground,
            driver.WriteRecords.Last(record => record.Text.Contains("[ Cancel ]", StringComparison.Ordinal)).Background);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}
