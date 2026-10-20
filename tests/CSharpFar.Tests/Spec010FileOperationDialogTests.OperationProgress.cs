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
    public void FileOperationUiRunner_HidesCursorWhileOperationRuns()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var runner = new FileOperationUiRunner(
            ModalTestHost.Create(screen),
            new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())),
            () => CSharpFarPaletteRegistry.Default,
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
    public void FileOperationUiRunner_RendersDeleteScanningWithoutCopyFields()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var runner = CreateRunner(screen, new DelayedProgressFileOperationService(
            new FileOperationProgress
            {
                Kind = FileOperationKind.Delete,
                Phase = FileOperationPhase.Scanning,
                CurrentPath = @"C:\source\folder",
                ItemsDone = 12,
                FoldersDone = 3,
                TotalBytesDone = 456,
            }));

        runner.Execute(new FileOperationRequest
        {
            Kind = FileOperationKind.Delete,
            Sources = [@"C:\source\folder"],
            Options = new FileOperationOptions(),
        });

        string text = driver.GetRegionText(new Rect(0, 0, 100, 30));
        Assert.Contains("Delete", text, StringComparison.Ordinal);
        Assert.Contains("Scanning files for deletion", text, StringComparison.Ordinal);
        Assert.Contains("Files found: 12", text, StringComparison.Ordinal);
        Assert.Contains("Folders found: 3", text, StringComparison.Ordinal);
        Assert.Contains("Bytes found: 456", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Destination:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Progress:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FileOperationUiRunner_RendersDeleteTotalsWithoutDestination()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var runner = CreateRunner(screen, new DelayedProgressFileOperationService(
            new FileOperationProgress
            {
                Kind = FileOperationKind.Delete,
                Phase = FileOperationPhase.Deleting,
                CurrentPath = @"C:\source\file.bin",
                CurrentDestinationPath = @"C:\destination\file.bin",
                ItemsDone = 1,
                ItemsTotal = 2,
                TotalBytesDone = 100,
                TotalBytesTotal = 200,
            }));

        runner.Execute(new FileOperationRequest
        {
            Kind = FileOperationKind.Delete,
            Sources = [@"C:\source\file.bin"],
            Options = new FileOperationOptions(),
        });

        string text = driver.GetRegionText(new Rect(0, 0, 100, 30));
        Assert.Contains("Deleting the file", text, StringComparison.Ordinal);
        Assert.Contains("Files: 1 / 2", text, StringComparison.Ordinal);
        Assert.Contains("Bytes: 100 / 200", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Destination:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("to C:\\destination", text, StringComparison.Ordinal);
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

}
