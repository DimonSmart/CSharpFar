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

    private static ConflictDialog CreateConflictDialog(ScreenRenderer screen)
    {
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        return new ConflictDialog(new DialogService(ModalTestHost.Create(screen), fields), fields);
    }

    private static ConsoleKeyInfo Ctrl(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: true);

    private static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char ch in text)
            driver.EnqueueKey(new ConsoleKeyInfo(ch, ConsoleKey.None, shift: false, alt: false, control: false));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static FileOperationUiRunner CreateRunner(ScreenRenderer screen, IFileOperationService service) =>
        new(
            ModalTestHost.Create(screen),
            new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())),
            () => CSharpFarPaletteRegistry.Default,
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
