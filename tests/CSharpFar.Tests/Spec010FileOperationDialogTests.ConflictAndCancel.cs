using CSharpFar.App.Commands;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed partial class Spec010FileOperationDialogTests
{
    [Fact]
    public void ConflictDialog_ReturnsOverwriteForO()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(new ConsoleKeyInfo('O', ConsoleKey.O, shift: true, alt: false, control: false));

        var decision = CreateConflictDialog(screen).Show(
            new FileOperationConflict
            {
                SourcePath = @"C:\src\a.txt",
                DestinationPath = @"C:\dst\a.txt",
                SourceSize = 3,
                DestinationSize = 5,
            });

        Assert.Equal(ConflictDecisionMode.Overwrite, decision.Mode);
    }


    [Fact]
    public void ConflictDialog_DirectoryConflictReturnsMergeForM()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(new ConsoleKeyInfo('M', ConsoleKey.M, shift: true, alt: false, control: false));

        var decision = CreateConflictDialog(screen)
            .Show(new FileOperationConflict
            {
                SourcePath = @"C:\src\Folder",
                DestinationPath = @"C:\dst\Folder",
                SourceIsDirectory = true,
                DestinationIsDirectory = true,
            });

        Assert.Equal(ConflictDecisionMode.Merge, decision.Mode);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("Replace", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, record => record.Text.Contains("Overwrite", StringComparison.Ordinal));
    }

    [Fact]
    public void ConflictDialog_RememberChoiceTurnsOverwriteIntoOverwriteAll()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var decision = CreateConflictDialog(screen).Show(
            new FileOperationConflict
            {
                SourcePath = @"C:\src\a.txt",
                DestinationPath = @"C:\dst\a.txt",
                SourceSize = 3,
                DestinationSize = 5,
            });

        Assert.Equal(ConflictDecisionMode.OverwriteAll, decision.Mode);
    }

    [Fact]
    public void ConflictDialog_DoesNotOfferAppend()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var decision = CreateConflictDialog(screen).Show(
            new FileOperationConflict
            {
                SourcePath = @"C:\src\a.txt",
                DestinationPath = @"C:\dst\a.txt",
                SourceSize = 3,
                DestinationSize = 5,
            });

        Assert.Equal(ConflictDecisionMode.Cancel, decision.Mode);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Append", StringComparison.Ordinal));
    }

    [Fact]
    public void ConflictDialog_RenameStaysInSameDialogAndReturnsEnteredDestination()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(new ConsoleKeyInfo('R', ConsoleKey.R, shift: true, alt: false, control: false));
        EnqueueText(driver, "_renamed");
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var decision = CreateConflictDialog(screen).Show(new FileOperationConflict
        {
            SourcePath = @"C:\src\a.txt",
            DestinationPath = @"C:\dst\a.txt",
            SourceSize = 3,
            DestinationSize = 5,
        });

        Assert.Equal(ConflictDecisionMode.Rename, decision.Mode);
        Assert.Equal(@"C:\dst\a.txt_renamed", decision.NewDestinationPath);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("New destination:", StringComparison.Ordinal));
    }

    [Fact]
    public void ConflictDialog_EscapeInRenameReturnsToActionSelection()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(new ConsoleKeyInfo('R', ConsoleKey.R, shift: true, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(new ConsoleKeyInfo('O', ConsoleKey.O, shift: true, alt: false, control: false));

        var decision = CreateConflictDialog(screen).Show(new FileOperationConflict
        {
            SourcePath = @"C:\src\a.txt",
            DestinationPath = @"C:\dst\a.txt",
        });

        Assert.Equal(ConflictDecisionMode.Overwrite, decision.Mode);
    }

    [Fact]
    public void ConflictDialog_CancelButtonInRenameReturnsToActionSelection()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(new ConsoleKeyInfo('R', ConsoleKey.R, shift: true, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(new ConsoleKeyInfo('O', ConsoleKey.O, shift: true, alt: false, control: false));

        var decision = CreateConflictDialog(screen).Show(new FileOperationConflict
        {
            SourcePath = @"C:\src\a.txt",
            DestinationPath = @"C:\dst\a.txt",
        });

        Assert.Equal(ConflictDecisionMode.Overwrite, decision.Mode);
    }

    [Fact]
    public void ConflictDialog_RenameValidationStaysInlineUntilDestinationIsValid()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        bool errorObserved = false;
        Action<FakeConsoleDriver>? continueAfterError = null;
        continueAfterError = currentDriver =>
        {
            if (currentDriver.PendingInputCount != 0)
            {
                currentDriver.BeforeReadInput = continueAfterError;
                return;
            }

            string text = currentDriver.GetRegionText(new Rect(0, 0, 100, 30));
            Assert.Contains("Destination must not be empty.", text, StringComparison.Ordinal);
            errorObserved = true;
            currentDriver.EnqueueKey(Ctrl(ConsoleKey.A));
            EnqueueText(currentDriver, @"C:\dst\b.txt");
            currentDriver.EnqueueKey(Key(ConsoleKey.Enter));
        };
        driver.BeforeReadInput = continueAfterError;
        driver.EnqueueKey(new ConsoleKeyInfo('R', ConsoleKey.R, shift: true, alt: false, control: false));
        driver.EnqueueKey(Ctrl(ConsoleKey.A));
        EnqueueText(driver, "   ");
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var decision = CreateConflictDialog(screen).Show(new FileOperationConflict
        {
            SourcePath = @"C:\src\a.txt",
            DestinationPath = @"C:\dst\a.txt",
        });

        Assert.True(errorObserved);
        Assert.Equal(ConflictDecisionMode.Rename, decision.Mode);
        Assert.Equal(@"C:\dst\b.txt", decision.NewDestinationPath);
    }

    [Fact]
    public void ConflictDialog_UnchangedRenameDestinationIsRejected()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        bool errorObserved = false;
        Action<FakeConsoleDriver>? continueAfterError = null;
        continueAfterError = currentDriver =>
        {
            if (currentDriver.PendingInputCount != 0)
            {
                currentDriver.BeforeReadInput = continueAfterError;
                return;
            }

            string text = currentDriver.GetRegionText(new Rect(0, 0, 100, 30));
            Assert.Contains("New destination must be different from the existing destination.", text, StringComparison.Ordinal);
            errorObserved = true;
            currentDriver.EnqueueKey(Key(ConsoleKey.Escape));
            currentDriver.EnqueueKey(new ConsoleKeyInfo('O', ConsoleKey.O, shift: true, alt: false, control: false));
        };
        driver.BeforeReadInput = continueAfterError;
        driver.EnqueueKey(new ConsoleKeyInfo('R', ConsoleKey.R, shift: true, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var decision = CreateConflictDialog(screen).Show(new FileOperationConflict
        {
            SourcePath = @"C:\src\a.txt",
            DestinationPath = @"C:\dst\a.txt",
        });

        Assert.True(errorObserved);
        Assert.Equal(ConflictDecisionMode.Overwrite, decision.Mode);
    }

    [Fact]
    public void ConflictDialog_ReenterRenamePreservesEditedDestination()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(new ConsoleKeyInfo('R', ConsoleKey.R, shift: true, alt: false, control: false));
        EnqueueText(driver, "_draft");
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(new ConsoleKeyInfo('R', ConsoleKey.R, shift: true, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var decision = CreateConflictDialog(screen).Show(new FileOperationConflict
        {
            SourcePath = @"C:\src\a.txt",
            DestinationPath = @"C:\dst\a.txt",
        });

        Assert.Equal(ConflictDecisionMode.Rename, decision.Mode);
        Assert.Equal(@"C:\dst\a.txt_draft", decision.NewDestinationPath);
    }

    [Fact]
    public void ConflictDialog_F10InRenameSubmitsInsteadOfCancelling()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(new ConsoleKeyInfo('R', ConsoleKey.R, shift: true, alt: false, control: false));
        EnqueueText(driver, "_f10");
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var decision = CreateConflictDialog(screen).Show(new FileOperationConflict
        {
            SourcePath = @"C:\src\a.txt",
            DestinationPath = @"C:\dst\a.txt",
        });

        Assert.Equal(ConflictDecisionMode.Rename, decision.Mode);
        Assert.Equal(@"C:\dst\a.txt_f10", decision.NewDestinationPath);
    }

    [Fact]
    public void ConflictDialog_RememberChoiceTurnsRenameIntoRenameAll()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(new ConsoleKeyInfo('R', ConsoleKey.R, shift: true, alt: false, control: false));

        var decision = CreateConflictDialog(screen).Show(new FileOperationConflict
        {
            SourcePath = @"C:\src\a.txt",
            DestinationPath = @"C:\dst\a.txt",
        });

        Assert.Equal(ConflictDecisionMode.RenameAll, decision.Mode);
        Assert.Null(decision.NewDestinationPath);
        Assert.DoesNotContain(driver.WriteRecords, record => record.Text.Contains("New destination:", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationCancelDialog_NoButtonSupportsMouseClick()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(new ConsoleKeyInfo('N', ConsoleKey.N, shift: true, alt: false, control: false));

        var modals = ModalTestHost.Create(screen);
        bool result = new OperationCancelDialog(new DialogService(modals, new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show();

        Assert.False(result);

        var buttonRecords = driver.WriteRecords
            .Where(r => r.Text.Contains("{ Yes }", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(buttonRecords);
        var buttonRecord = buttonRecords[^1];
        int bottomFrameRow = driver.WriteRecords
            .Where(r => r.Text.Contains('└') || r.Text.Contains('╚'))
            .Select(r => r.Y)
            .DefaultIfEmpty(-1)
            .Max();

        Assert.True(bottomFrameRow > buttonRecord.Y);
        Assert.DoesNotContain('└', buttonRecord.Text);
        Assert.DoesNotContain('┘', buttonRecord.Text);
        Assert.DoesNotContain('╚', buttonRecord.Text);
        Assert.DoesNotContain('╝', buttonRecord.Text);
    }

}
