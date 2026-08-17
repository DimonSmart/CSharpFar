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

public sealed class Spec010FileOperationDialogTests
{
    [Fact]
    public void ShowCopy_ContextualHelpUsesRealTemporarySurfaceAndPreservesFormState()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        UiTestHost host = UiTestHost.Create(driver);
        EnqueueText(driver, "_edited");
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F1));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(
            new DialogService(host.ModalDialogs, new FormFieldFactory(TextFieldHistoryTestProvider.Create())),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create()),
            topic => new HelpViewer(host.Surfaces).Show(topic)).ShowCopy(
                [@"C:\source\analysis_options.yaml"],
                @"C:\destination",
                new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(@"C:\destination_edited", result.Destination);
        Assert.True(result.UseDestinationTemplate);
        Assert.Equal(CopyMode.Reliable, result.Options.CopyMode);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("CSharpFar — Copy", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("Copy", StringComparison.Ordinal) && record.Text.Contains("Help", StringComparison.Ordinal) && record.Text.Contains("Cancel", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowCopy_ContextualHelpReturnsBeforeDialogCancel()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        UiTestHost host = UiTestHost.Create(driver);
        EnqueueText(driver, "_edited");
        driver.EnqueueKey(Key(ConsoleKey.F1));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new FileOperationDialog(
            new DialogService(host.ModalDialogs, new FormFieldFactory(TextFieldHistoryTestProvider.Create())),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create()),
            topic => new HelpViewer(host.Surfaces).Show(topic)).ShowCopy(
                [@"C:\source\a.txt"],
                @"C:\destination",
                new FileOperationOptions());

        Assert.Null(result);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("CSharpFar — Copy", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowCopy_ReturnsDestinationAndDefaultOptionsFromSingleDialog()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(@"C:\destination", result.Destination);
        Assert.False(result.UseDestinationTemplate);
        Assert.Equal(FileSecurityMode.Default, result.Options.SecurityMode);
        Assert.Null(result.Options.FileMask);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Already existing files:", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Copy mode:", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Reliable", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Fast salvage", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Access rights:", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("(x) Default ( ) Copy", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Inherit", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Preserve attributes", StringComparison.Ordinal));
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

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(@"C:\destination", result.Destination);
    }

    [Fact]
    public void ShowCopy_UseTemplateIsExplicitAndDisabledByDefault()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination\{name}_OLD{ext}",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.True(result.UseDestinationTemplate);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Use template", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowCopy_EscapeCancelsDialog()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.Null(result);
    }

    [Fact]
    public void ShowCopy_TabMovesKeyboardFocusToFooterButtons()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        for (int i = 0; i < 9; i++)
            driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(@"C:\destination", result.Destination);
    }

    [Fact]
    public void ShowCopy_FocusedFooterCancelButtonActivatesWithKeyboard()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        for (int i = 0; i < 9; i++)
            driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.Null(result);
    }

    [Fact]
    public void ShowCopy_MovesCursorFromTextInputToOptionRow()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            Assert.True(currentDriver.CursorVisible);
            var inputCursor = (currentDriver.CursorX, currentDriver.CursorY);
            currentDriver.EnqueueKey(Key(ConsoleKey.DownArrow));
            currentDriver.BeforeReadInput = nextDriver =>
            {
                Assert.True(nextDriver.CursorVisible);
                Assert.NotEqual(inputCursor, (nextDriver.CursorX, nextDriver.CursorY));
                nextDriver.EnqueueKey(Key(ConsoleKey.F10));
            };
        };

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
    }

    [Fact]
    public void ShowCopy_MovesCursorWhenTabMovesFocusFromTextInputToOptionRow()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            Assert.True(currentDriver.CursorVisible);
            var inputCursor = (currentDriver.CursorX, currentDriver.CursorY);
            currentDriver.EnqueueKey(Key(ConsoleKey.Tab));
            currentDriver.BeforeReadInput = nextDriver =>
            {
                Assert.True(nextDriver.CursorVisible);
                Assert.NotEqual(inputCursor, (nextDriver.CursorX, nextDriver.CursorY));
                nextDriver.EnqueueKey(Key(ConsoleKey.F10));
            };
        };

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
    }

    [Fact]
    public void ShowCopy_CollectsFilterInSameDialog()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        for (int i = 0; i < 8; i++)
            driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Backspace));
        EnqueueText(driver, "*.txt");
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal("*.txt", result.Options.FileMask);
    }

    [Fact]
    public void ShowCopy_OffersCopyModeNormalReliableFastSalvage()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        for (int i = 0; i < 2; i++)
            driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(CopyMode.FastSalvage, result.Options.CopyMode);
        Assert.Equal(ConflictDecisionMode.Ask, result.Options.DefaultConflictDecision);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Normal", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Reliable", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Fast salvage", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowCopy_ReliableIsNotInConflictDecisionList()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        var conflictRows = driver.WriteRecords.Where(r =>
            r.Text.Contains("Ask", StringComparison.Ordinal) &&
            r.Text.Contains("Overwrite", StringComparison.Ordinal));
        Assert.DoesNotContain(conflictRows, r => r.Text.Contains("Reliable", StringComparison.Ordinal));
        Assert.DoesNotContain(conflictRows, r => r.Text.Contains("Fast salvage", StringComparison.Ordinal));
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

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(ConflictDecisionMode.Rename, result.Options.DefaultConflictDecision);
    }

    [Fact]
    public void ShowCopy_MouseSelectsCopyAccessRightsMode()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(record =>
                record.Text.Contains("Access rights:", StringComparison.Ordinal));
            int x = row.X + row.Text.IndexOf("Copy", StringComparison.Ordinal);
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(x, row.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            currentDriver.EnqueueKey(Key(ConsoleKey.F10));
        };

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal(FileSecurityMode.CopyAccessControl, result.Options.SecurityMode);
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

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.False(result.Options.PreserveTimestamps);
    }

    [Fact]
    public void ShowMove_DoesNotOfferCopyMode()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowMove(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions
            {
                CopyMode = CopyMode.Reliable,
            });

        Assert.NotNull(result);
        Assert.Equal(ConflictDecisionMode.Ask, result.Options.DefaultConflictDecision);
        Assert.Equal(CopyMode.Normal, result.Options.CopyMode);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Copy mode:", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Reliable", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowRename_UsesRenameTitleAndDoesNotOfferAppend()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowRename(
            @"C:\source\old.txt",
            "old.txt",
            new FileOperationOptions());

        Assert.NotNull(result);
        Assert.Equal("old.txt", result.Destination);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Rename", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Copy mode:", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Only newer", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Access rights", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Use filter", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Append", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowCopy_CancelButtonSupportsMouseClick()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(record =>
                record.Text.Contains("Cancel", StringComparison.Ordinal));
            int x = row.X + row.Text.IndexOf("Cancel", StringComparison.Ordinal);
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(x, row.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(x, row.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
        };

        var result = new FileOperationDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).ShowCopy(
            [@"C:\source\a.txt"],
            @"C:\destination",
            new FileOperationOptions());

        Assert.Null(result);
    }

    [Fact]
    public void ProgressDialog_RendersFarStyleProgressBarWithoutHashCharacters()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);

        RenderProgress(screen, @"C:\dst",
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
    public void FileOperationUiRunner_HidesCursorWhileOperationRuns()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var runner = new FileOperationUiRunner(
            ModalTestHost.Create(screen),
            new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())),
            () => PaletteRegistry.Default,
            new NoOpFileOperationService(),
            () => true,
            new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

        runner.Execute(new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [@"C:\source\a.txt"],
            Destination = @"C:\destination",
            Options = new FileOperationOptions(),
        });

        Assert.False(driver.CursorVisible);
        Assert.True(driver.SetCursorVisibleCallCount > 0);
    }

    [Fact]
    public void FileOperationUiRunner_RendersProgressAndCompletesWithoutConsoleInput()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var service = new DelayedProgressFileOperationService(
            new FileOperationProgress
            {
                Kind = FileOperationKind.Copy,
                Phase = FileOperationPhase.Copying,
                CurrentPath = @"C:\source\a.txt",
                CurrentDestinationPath = @"C:\destination\a.txt",
                CurrentBytesDone = 5,
                CurrentBytesTotal = 10,
                TotalBytesDone = 5,
                TotalBytesTotal = 10,
                ItemsDone = 1,
                ItemsTotal = 1,
            });
        var runner = CreateRunner(screen, service);

        FileOperationResult result = runner.Execute(CopyRequest());

        Assert.False(result.Cancelled);
        string text = driver.GetRegionText(new Rect(0, 0, 100, 30));
        Assert.Contains("Copying the file", text, StringComparison.Ordinal);
        Assert.Contains(@"C:\source\a.txt", text, StringComparison.Ordinal);
        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void FileOperationUiRunner_EscapeDuringScanCancelsWithoutConfirmation()
    {
        bool cancelled = false;
        bool confirmationRequested = false;

        bool accepted = FileOperationUiRunner.HandleCancellation(
            new FileOperationUiRunner.FileOperationProgressFrame(
                new FileOperationProgress
                {
                    Kind = FileOperationKind.Copy,
                    Phase = FileOperationPhase.Scanning,
                    CurrentPath = @"C:\source",
                },
                ShowTotalProgress: true,
                FileOperationUiRunner.FileOperationUiStatus.Running),
            cancelImmediately: () => cancelled = true,
            requestConfirmation: () => confirmationRequested = true);

        Assert.True(accepted);
        Assert.True(cancelled);
        Assert.False(confirmationRequested);
    }

    [Fact]
    public void FileOperationUiRunner_CancellationUsesCommittedScanFrameWhenNewOperationProgressExists()
    {
        var committedFrame = new FileOperationUiRunner.FileOperationProgressFrame(
            new FileOperationProgress
            {
                Kind = FileOperationKind.Copy,
                Phase = FileOperationPhase.Scanning,
                CurrentPath = @"C:\source",
            },
            ShowTotalProgress: true,
            FileOperationUiRunner.FileOperationUiStatus.Running);
        var preparedProgress = new FileOperationProgress
        {
            Kind = FileOperationKind.Copy,
            Phase = FileOperationPhase.Copying,
            CurrentPath = @"C:\source\a.txt",
        };
        bool cancelled = false;
        bool confirmationShown = false;

        bool accepted = FileOperationUiRunner.HandleCancellation(
            committedFrame,
            cancelImmediately: () => cancelled = true,
            requestConfirmation: () => confirmationShown = preparedProgress.Phase == FileOperationPhase.Copying);

        Assert.True(accepted);
        Assert.True(cancelled);
        Assert.False(confirmationShown);
    }

    [Fact]
    public void FileOperationUiRunner_EscapeDuringOperationUsesConfirmation()
    {
        bool cancelledImmediately = false;
        bool confirmationRequested = false;

        bool accepted = FileOperationUiRunner.HandleCancellation(
            new FileOperationUiRunner.FileOperationProgressFrame(
                new FileOperationProgress
                {
                    Kind = FileOperationKind.Copy,
                    Phase = FileOperationPhase.Copying,
                    CurrentPath = @"C:\source\a.txt",
                },
                ShowTotalProgress: true,
                FileOperationUiRunner.FileOperationUiStatus.Running),
            cancelImmediately: () => cancelledImmediately = true,
            requestConfirmation: () => confirmationRequested = true);

        Assert.True(accepted);
        Assert.False(cancelledImmediately);
        Assert.True(confirmationRequested);
    }

    [Fact]
    public void FileOperationUiRunner_PendingConflictShowsDialogAndContinuesAfterDecision()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var service = new ConflictFileOperationService();
        var runner = CreateRunner(screen, service);
        bool conflictDecisionQueued = false;
        driver.Wrote += record =>
        {
            if (!conflictDecisionQueued && record.Text.Contains(@"C:\destination\a.txt", StringComparison.Ordinal))
            {
                conflictDecisionQueued = true;
                driver.EnqueueKey(new ConsoleKeyInfo('O', ConsoleKey.O, shift: true, alt: false, control: false));
            }
        };

        FileOperationResult result = runner.Execute(CopyRequest());

        Assert.False(result.Cancelled);
        Assert.Equal(ConflictDecisionMode.Overwrite, service.Decision?.Mode);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains(@"C:\destination\a.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void FileOperationUiRunner_UiFailureUnblocksPendingConflict()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var service = new ConflictFileOperationService();
        var runner = CreateRunner(screen, service);
        driver.Wrote += record =>
        {
            if (record.Text.Contains(@"C:\destination\a.txt", StringComparison.Ordinal))
                throw new InvalidOperationException("render failed");
        };

        Assert.Throws<InvalidOperationException>(() => runner.Execute(CopyRequest()));

        Assert.True(service.DecisionReady.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(ConflictDecisionMode.Cancel, service.Decision?.Mode);
    }

    [Fact]
    public void FileOperationUiRunner_UiFailureWaitsForOperationCleanupAndReleasesPause()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var service = new CleanupAfterCancellationFileOperationService();
        var runner = CreateRunner(screen, service);
        var uiException = new InvalidOperationException("cancel dialog render failed");
        driver.BeforeReadInput = currentDriver =>
            currentDriver.BeforeReadInput = nextDriver =>
            {
                nextDriver.EnqueueKey(Key(ConsoleKey.Escape));
                nextDriver.BeforeReadInput = _ => throw uiException;
            };

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => runner.Execute(CopyRequest()));

        Assert.Same(uiException, thrown);
        Assert.True(service.Completed);
        Assert.True(service.PauseReleased);
    }

    [Fact]
    public async Task FileOperationUiRunner_RendersStoppingBeforeCancelledOperationCleanupCompletes()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var service = new CleanupGateFileOperationService();
        var runner = CreateRunner(screen, service);
        driver.Wrote += record =>
        {
            if (record.Text.Contains("really", StringComparison.OrdinalIgnoreCase))
                driver.EnqueueKey(Key(ConsoleKey.Enter));

            if (record.Text.Contains("Stopping", StringComparison.Ordinal))
                service.StoppingRendered.TrySetResult();
        };
        driver.BeforeReadInput = currentDriver =>
        {
            service.CopyingStarted.Task.GetAwaiter().GetResult();
            currentDriver.EnqueueKey(Key(ConsoleKey.Escape));
        };

        Task operation = Task.Run(() => Assert.Throws<OperationCanceledException>(() => runner.Execute(CopyRequest())));

        await service.CopyingStarted.Task;
        await service.StoppingRendered.Task;
        await service.CleanupStarted.Task;

        Assert.True(service.StoppingWasRenderedBeforeCleanup);
        Assert.False(service.CleanupCompleted.Task.IsCompleted);

        service.AllowCleanup.TrySetResult();
        await operation;

        Assert.True(service.CleanupCompleted.Task.IsCompleted);
    }

    [Fact]
    public void ProgressDialog_RendersResumeValidationState()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);

        RenderProgress(screen, @"C:\dst",
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

        RenderProgress(screen, @"C:\dst",
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

        RenderProgress(screen, @"C:\dst",
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

        var conflictModals = ModalTestHost.Create(screen);
        var decision = new ConflictDialog(new DialogService(conflictModals, new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
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

        var conflictModals = ModalTestHost.Create(screen);
        var decision = new ConflictDialog(new DialogService(conflictModals, new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
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

        var conflictModals = ModalTestHost.Create(screen);
        var decision = new ConflictDialog(new DialogService(conflictModals, new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
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

    private static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char ch in text)
            driver.EnqueueKey(new ConsoleKeyInfo(ch, ConsoleKey.None, shift: false, alt: false, control: false));
    }

    private static void RenderProgress(
        ScreenRenderer screen,
        string destination,
        FileOperationProgress progress,
        bool showTotalProgress)
    {
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        using var overlay = composition.PushOverlay(context =>
            new ProgressDialog(context.Canvas, destination).Render(
                context,
                progress,
                showTotalProgress,
                FileOperationUiRunner.FileOperationUiStatus.Running));
        composition.Render();
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static FileOperationUiRunner CreateRunner(ScreenRenderer screen, IFileOperationService service) =>
        new(
            ModalTestHost.Create(screen),
            new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())),
            () => PaletteRegistry.Default,
            service,
            () => true,
            new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

    private static FileOperationRequest CopyRequest() =>
        new()
        {
            Kind = FileOperationKind.Copy,
            Sources = [@"C:\source\a.txt"],
            Destination = @"C:\destination",
            Options = new FileOperationOptions(),
        };

    private sealed class DelayedProgressFileOperationService(FileOperationProgress progressSnapshot) : IFileOperationService
    {
        public bool SupportsRecycleBin => true;

        public async Task<FileOperationResult> ExecuteAsync(
            FileOperationRequest request,
            IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(progressSnapshot);
            await Task.Delay(30, cancellationToken);
            return new FileOperationResult { Kind = request.Kind, Errors = [] };
        }
    }

    private sealed class ConflictFileOperationService : IFileOperationService
    {
        public bool SupportsRecycleBin => true;

        public ManualResetEventSlim DecisionReady { get; } = new();

        public FileOperationConflictDecision? Decision { get; private set; }

        public Task<FileOperationResult> ExecuteAsync(
            FileOperationRequest request,
            IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver,
            CancellationToken cancellationToken = default)
        {
            Decision = conflictResolver.Resolve(new FileOperationConflict
            {
                SourcePath = @"C:\source\a.txt",
                DestinationPath = @"C:\destination\a.txt",
                SourceSize = 3,
                DestinationSize = 5,
            });
            DecisionReady.Set();

            return Task.FromResult(new FileOperationResult
            {
                Kind = request.Kind,
                Cancelled = Decision.Mode == ConflictDecisionMode.Cancel,
                Errors = [],
            });
        }
    }

    private sealed class CleanupAfterCancellationFileOperationService : IFileOperationService
    {
        private IFileOperationPauseController? _pauseController;

        public bool SupportsRecycleBin => true;

        public bool Completed { get; private set; }

        public bool PauseReleased
        {
            get
            {
                using var probe = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                try
                {
                    _pauseController?.WaitIfPaused(probe.Token);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        public async Task<FileOperationResult> ExecuteAsync(
            FileOperationRequest request,
            IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver,
            CancellationToken cancellationToken = default)
        {
            _pauseController = request.PauseController;
            progress?.Report(new FileOperationProgress
            {
                Kind = request.Kind,
                Phase = FileOperationPhase.Copying,
                CurrentPath = @"C:\source\a.txt",
                CurrentDestinationPath = @"C:\destination\a.txt",
            });

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await Task.Delay(40);
                Completed = true;
                throw;
            }

            throw new InvalidOperationException("Cancellation was expected.");
        }
    }

    private sealed class CleanupGateFileOperationService : IFileOperationService
    {
        public TaskCompletionSource CopyingStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StoppingRendered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CleanupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CleanupCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool StoppingWasRenderedBeforeCleanup { get; private set; }

        public bool SupportsRecycleBin => true;

        public async Task<FileOperationResult> ExecuteAsync(
            FileOperationRequest request,
            IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new FileOperationProgress
            {
                Kind = request.Kind,
                Phase = FileOperationPhase.Copying,
                CurrentPath = @"C:\source\a.txt",
                CurrentDestinationPath = @"C:\destination\a.txt",
            });
            CopyingStarted.TrySetResult();

            var cancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => cancellation.TrySetResult());
            await cancellation.Task;
            CancellationObserved.TrySetResult();
            StoppingWasRenderedBeforeCleanup = StoppingRendered.Task.IsCompleted;
            CleanupStarted.TrySetResult();
            await AllowCleanup.Task;
            CleanupCompleted.TrySetResult();
            return new FileOperationResult { Kind = request.Kind, Cancelled = true, Errors = [] };
        }
    }
}
