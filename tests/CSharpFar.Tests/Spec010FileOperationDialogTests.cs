using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class Spec010FileOperationDialogTests
{
    [Fact]
    public void ShowCopy_ReturnsDestinationAndDefaultOptionsFromSingleDialog()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(@"C:\destination", result.Destination);
        Assert.Null(result.Options.FileMask);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Already existing files:", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Paranoid", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Access rights:", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Use filter", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Trim() == "*");
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Process multiple destinations", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowCopy_EnterConfirmsDialog()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(@"C:\destination", result.Destination);
    }

    [Fact]
    public void ShowCopy_EscapeCancelsDialog()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.Null(result);
    }

    [Fact]
    public void ShowCopy_TabDoesNotMoveKeyboardFocusToFooterButtons()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        for (int i = 0; i < 12; i++)
            driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(@"C:\destination", result.Destination);
    }

    [Fact]
    public void ShowCopy_CollectsFilterInSameDialog()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        for (int i = 0; i < 5; i++)
            driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Backspace));
        EnqueueText(driver, "*.txt");
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal("*.txt", result.Options.FileMask);
    }

    [Fact]
    public void ShowCopy_OffersParanoidForCopyOnly()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        for (int i = 0; i < 5; i++)
            driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(ConflictDecisionMode.ResumeWithTailValidation, result.Options.DefaultConflictDecision);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Paranoid", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowCopy_MouseSelectsParanoidConflictMode()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueInput(new MouseConsoleInputEvent(28, 13, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(ConflictDecisionMode.ResumeWithTailValidation, result.Options.DefaultConflictDecision);
    }

    [Fact]
    public void ShowCopy_MouseSelectsFirstConflictOptionRow()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(record =>
                record.Text.Contains("Rename", StringComparison.Ordinal) &&
                record.Text.Contains("Overwrite", StringComparison.Ordinal));
            int x = row.X + row.Text.IndexOf("Rename", StringComparison.Ordinal);
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(x, row.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            currentDriver.EnqueueKey(Key(ConsoleKey.F10));
        };

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(ConflictDecisionMode.Rename, result.Options.DefaultConflictDecision);
    }

    [Fact]
    public void ShowCopy_MouseSelectsAccessRightsMode()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(record =>
                record.Text.Contains("Access rights:", StringComparison.Ordinal));
            int x = row.X + row.Text.IndexOf("Inherit", StringComparison.Ordinal);
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(x, row.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            currentDriver.EnqueueKey(Key(ConsoleKey.F10));
        };

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(FileSecurityMode.Inherit, result.Options.SecurityMode);
    }

    [Fact]
    public void ShowCopy_MouseClickCheckboxTogglesPreserveTimestampsOff()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(record =>
                record.Text.Contains("Preserve all timestamps", StringComparison.Ordinal));
            int textX = row.X + row.Text.IndexOf("Preserve all timestamps", StringComparison.Ordinal);
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(textX, row.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            currentDriver.EnqueueKey(Key(ConsoleKey.F10));
        };

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.False(result.Options.PreserveTimestamps);
    }

    [Fact]
    public void ShowMove_DoesNotOfferParanoidForMove()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(screen).ShowMove(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions
            {
                DefaultConflictDecision = ConflictDecisionMode.ResumeWithTailValidation,
            });

        Assert.NotNull(result);
        Assert.Equal(ConflictDecisionMode.Ask, result.Options.DefaultConflictDecision);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Paranoid", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowRename_UsesRenameTitleAndDoesNotOfferAppend()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(screen).ShowRename(
            @"C:\source\old.txt",
            "old.txt",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal("old.txt", result.Destination);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Rename", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Only newer", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Access rights", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Use filter", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Append", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowCopy_CancelButtonSupportsMouseClick()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueInput(new MouseConsoleInputEvent(52, 23, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        var result = new FileOperationDialog(screen).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.Null(result);
    }

    [Fact]
    public void BuildRows_ReusesDestinationAndFilterTextInputRowState()
    {
        var destinationRowState = new TextInputRowState();
        var filterRowState = new TextInputRowState();
        var firstRows = BuildFileOperationRows(destinationRowState, filterRowState);
        var secondRows = BuildFileOperationRows(destinationRowState, filterRowState);

        var firstInputs = firstRows.OfType<TextInputRow>().ToArray();
        var secondInputs = secondRows.OfType<TextInputRow>().ToArray();

        Assert.Same(destinationRowState, firstInputs[0].State);
        Assert.Same(filterRowState, firstInputs[1].State);
        Assert.Same(destinationRowState, secondInputs[0].State);
        Assert.Same(filterRowState, secondInputs[1].State);
    }

    [Fact]
    public void CreateFolderDialog_RendersMakeFolderWindowWithoutLinkOptions()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        EnqueueText(driver, "NewDir");
        driver.EnqueueKey(Key(ConsoleKey.F10));

        string? result = new CreateFolderDialog(screen).Show();

        Assert.Equal("NewDir", result);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Make folder", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Create the folder:", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("{ OK }", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("[ Cancel ]", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains('╔'));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Link type", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Target", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("multiple", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateFolderDialog_CancelButtonSupportsMouseClick()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueInput(new MouseConsoleInputEvent(50, 16, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        string? result = new CreateFolderDialog(screen).Show();

        Assert.Null(result);
    }

    [Fact]
    public void ProgressDialog_RendersFarStyleProgressBarWithoutHashCharacters()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);

        new ProgressDialog(screen, @"C:\dst").Update(
            new FileOperationProgress
            {
                Kind = FileOperationKind.Copy,
                Phase = FileOperationPhase.Copying,
                CurrentPath = @"C:\src\a.txt",
                CurrentDestinationPath = @"C:\dst\a.txt",
                CurrentBytesDone = 5,
                CurrentBytesTotal = 10,
                TotalBytesDone = 5,
                TotalBytesTotal = 20,
                ItemsDone = 1,
                ItemsTotal = 2,
                Elapsed = TimeSpan.FromSeconds(1),
            },
            showTotalProgress: true);

        string text = driver.GetRegionText(new Rect(0, 0, 100, 30));
        Assert.Contains('█', text);
        Assert.Contains('░', text);
        Assert.DoesNotContain('#', text);
    }

    [Fact]
    public void ProgressDialog_RendersResumeValidationState()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);

        new ProgressDialog(screen, @"C:\dst").Update(
            new FileOperationProgress
            {
                Kind = FileOperationKind.Copy,
                Phase = FileOperationPhase.Validating,
                StatusMessage = "Tail mismatch detected",
                CurrentPath = @"C:\src\a.bin",
                CurrentDestinationPath = @"C:\dst\a.bin",
                CurrentBytesDone = 1024,
                CurrentBytesTotal = 4096,
                TotalBytesDone = 1024,
                TotalBytesTotal = 4096,
                ResumeOffset = 1024,
                ResumeRollbackBytes = 512,
                ItemsDone = 0,
                ItemsTotal = 1,
                Elapsed = TimeSpan.FromSeconds(1),
            },
            showTotalProgress: true);

        string text = driver.GetRegionText(new Rect(0, 0, 100, 30));
        Assert.Contains("Tail mismatch detected", text, StringComparison.Ordinal);
        Assert.Contains("Resume offset:", text, StringComparison.Ordinal);
        Assert.Contains("Rollback:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressDialog_RendersDeleteSpecificProgress()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);

        new ProgressDialog(screen, @"C:\dst").Update(
            new FileOperationProgress
            {
                Kind = FileOperationKind.Delete,
                Phase = FileOperationPhase.Deleting,
                CurrentPath = @"C:\src\a.bin",
                CurrentDestinationPath = @"C:\dst\a.bin",
                CurrentBytesDone = 1024,
                CurrentBytesTotal = 4096,
                TotalBytesDone = 1024,
                TotalBytesTotal = 4096,
                ResumeOffset = 1024,
                ResumeRollbackBytes = 512,
                ItemsDone = 1,
                ItemsTotal = 2,
                Elapsed = TimeSpan.FromSeconds(1),
            },
            showTotalProgress: true);

        string text = driver.GetRegionText(new Rect(0, 0, 100, 30));
        Assert.Contains("Delete", text, StringComparison.Ordinal);
        Assert.Contains("Deleting the file", text, StringComparison.Ordinal);
        Assert.Contains(@"C:\src\a.bin", text, StringComparison.Ordinal);
        Assert.Contains("Files:", text, StringComparison.Ordinal);
        Assert.Contains("Bytes:", text, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\dst\a.bin", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Resume offset:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Rollback:", text, StringComparison.Ordinal);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Trim() == "to");
    }

    [Fact]
    public void ProgressDialog_RendersScanningWithDoubleBorderAndBytesAboveBottomFrame()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);

        new ProgressDialog(screen, @"C:\dst").Update(
            new FileOperationProgress
            {
                Kind = FileOperationKind.Copy,
                Phase = FileOperationPhase.Scanning,
                CurrentPath = @"C:\src\YouTube",
                ItemsDone = 2651,
                FoldersDone = 123,
                TotalBytesDone = 96081696632,
            },
            showTotalProgress: true);

        string[] rows = Enumerable.Range(0, 30)
            .Select(driver.GetRow)
            .ToArray();
        int bytesRow = Array.FindIndex(rows, row => row.Contains("Bytes:", StringComparison.Ordinal));
        int bottomFrameRow = Array.FindIndex(rows, row => row.Contains('╚'));

        Assert.Contains(rows, row => row.Contains('╔'));
        Assert.True(bytesRow >= 0);
        Assert.True(bottomFrameRow > bytesRow);
        Assert.DoesNotContain('╚', rows[bytesRow]);
        Assert.DoesNotContain('╝', rows[bytesRow]);
    }

    [Fact]
    public void ConflictDialog_ReturnsOverwriteForO()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(new ConsoleKeyInfo('O', ConsoleKey.O, shift: true, alt: false, control: false));

        var decision = new ConflictDialog(screen).Show(
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
    public void ConflictDialog_RememberChoiceTurnsOverwriteIntoOverwriteAll()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var decision = new ConflictDialog(screen).Show(
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
    public void ConflictDialog_AppendButtonSupportsMouseClick()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueInput(new MouseConsoleInputEvent(58, 18, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        var decision = new ConflictDialog(screen).Show(
            new FileOperationConflict
            {
                SourcePath = @"C:\src\a.txt",
                DestinationPath = @"C:\dst\a.txt",
                SourceSize = 3,
                DestinationSize = 5,
            });

        Assert.Equal(ConflictDecisionMode.Append, decision.Mode);
    }

    [Fact]
    public void OperationCancelDialog_NoButtonSupportsMouseClick()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueInput(new MouseConsoleInputEvent(53, 16, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        bool result = new OperationCancelDialog(screen).Show();

        Assert.False(result);

        var buttonRecord = Assert.Single(driver.WriteRecords, r => r.Text.Contains("{ Yes }", StringComparison.Ordinal));
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

    private static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char ch in text)
            driver.EnqueueKey(new ConsoleKeyInfo(ch, ConsoleKey.None, shift: false, alt: false, control: false));
    }

    private static IReadOnlyList<IFormRow> BuildFileOperationRows(TextInputRowState destinationRowState, TextInputRowState filterRowState)
    {
        var destination = new CommandLineState();
        var filter = new CommandLineState();
        filter.SetText("*");
        var conflictChoice = new ChoiceRow<ConflictDecisionMode>([ConflictDecisionMode.Ask], static mode => mode.ToString());
        var method = typeof(FileOperationDialog).GetMethod(
            "BuildRows",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("FileOperationDialog.BuildRows was not found.");

        return (IReadOnlyList<IFormRow>)method.Invoke(
            null,
            [
                "Copy to:",
                destination,
                filter,
                new SingleLineTextHistoryState(),
                new SingleLineTextHistoryState(),
                destinationRowState,
                filterRowState,
                new ChoiceFormRow<FileSecurityMode>(
                    new ChoiceRow<FileSecurityMode>([FileSecurityMode.Default], static mode => mode.ToString()),
                    "Access rights:"),
                new ChoiceFormRow<ConflictDecisionMode>(conflictChoice, string.Empty, 0, 1),
                new ChoiceFormRow<ConflictDecisionMode>(conflictChoice, string.Empty, 1, 1, isFocusable: false),
                new CheckBoxRow(new CheckBoxLine("Preserve all timestamps")),
                new CheckBoxRow(new CheckBoxLine("Copy contents of symbolic links")),
                new CheckBoxRow(new CheckBoxLine("Use filter", value: true)),
                true,
            ])!;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
