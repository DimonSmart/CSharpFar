using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Ui.Demo;

internal enum PullOutcome { Completed, Cancelled, Failed }

internal sealed class ShowcaseWorkflows
{
    private readonly DialogService _dialogs;
    private readonly object _progressGate = new();
    private PullProgress _progress = new(0, "Connecting to origin…", false);

    public ShowcaseWorkflows(DialogService dialogs) => _dialogs = dialogs;

    public PullOutcome Pull(bool simulateFailure)
    {
        _progress = new(0, "Connecting to origin…", false);
        var form = new ScrollableFormDialog();
        form.SetRows([], [FormControls.Buttons(DialogButton.Cancel())]);
        try
        {
            return _dialogs.Operation(
                new OperationDialogOptions(
                    new CompositeDialogOptions("Pull from origin", 64, 13, 42, 10) { ResizeMode = DialogResizeMode.Both },
                    TimeSpan.FromMilliseconds(100)),
                async cancellationToken =>
                {
                    string[] stages = ["Fetching objects", "Resolving deltas", "Updating main", "Refreshing commit list"];
                    for (int step = 1; step <= 20; step++)
                    {
                        await Task.Delay(90, cancellationToken).ConfigureAwait(false);
                        lock (_progressGate) _progress = new(step * 5, stages[Math.Min(stages.Length - 1, (step - 1) / 5)], false);
                        if (simulateFailure && step == 12)
                            throw new InvalidOperationException("The fake remote rejected the demonstration pull.");
                    }
                    return PullOutcome.Completed;
                },
                form,
                new PullProgressContent(() => { lock (_progressGate) return _progress; }),
                () => { lock (_progressGate) return _progress.Cancelling ? "Cancellation requested — cleaning up…" : $"{_progress.Percent}% complete"; },
                new Dictionary<ConsoleKey, string> { [ConsoleKey.Escape] = "cancel" },
                synchronize: () => true,
                handle: e => e.Kind is CompositeDialogEventKind.Cancelled || e.Command == "cancel"
                    ? OperationDialogOutcome<PullOutcome>.RequestImmediateCancellation
                    : OperationDialogOutcome<PullOutcome>.ContinueNoChange,
                complete: result => result,
                onCancellationRequested: () => { lock (_progressGate) _progress = _progress with { Cancelling = true, Stage = "Cleaning temporary fetch state" }; });
        }
        catch (OperationCanceledException)
        {
            return PullOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            _dialogs.Message("Pull failed", ex.Message);
            return PullOutcome.Failed;
        }
        finally
        {
            lock (_progressGate) _progress = new(0, "Idle", false);
        }
    }

    public string MergeConflict()
    {
        var actions = FormControls.Buttons(
            DialogButton.Default("local", "Keep Local", 'L'),
            DialogButton.Action("remote", "Keep Remote", 'R'),
            DialogButton.Cancel());
        return _dialogs.Form(
            new FormDialogOptions("Merge Conflict", 70, 14) { Appearance = DialogAppearance.Warning, InitialFocus = actions },
            () =>
            [
                FormControls.Label("Both branches changed the same lines", TextAlignment.Center),
                FormControls.Separator(),
                FormControls.Value("File", () => "src/Widgets/UnicodeRenderer.cs"),
                FormControls.Value("Local", () => "Preserve grapheme clusters"),
                FormControls.Value("Remote", () => "Normalize combining marks"),
                FormControls.Spacer(),
            ],
            () => [actions],
            e => e.IsCancelled ? FormDialogOutcome<string>.Complete("cancel")
                : e.Command is { } command ? FormDialogOutcome<string>.Complete(command)
                : FormDialogOutcome<string>.Continue());
    }

    private sealed record PullProgress(int Percent, string Stage, bool Cancelling);

    private sealed class PullProgressContent(Func<PullProgress> state) : ICompositeDialogContent
    {
        public ICompositeDialogContentFrame CalculateFrame(Rect bounds) => new Frame(bounds);
        public void Render(IUiCanvas canvas, ICompositeDialogContentFrame raw)
        {
            var frame = (Frame)raw;
            PullProgress value = state();
            canvas.FillRegion(frame.Bounds, DialogStyles.Fill);
            Write(canvas, frame.Bounds, 0, value.Cancelling ? "Stopping fake pull safely…" : value.Stage);
            int barWidth = Math.Max(0, frame.Bounds.Width - 2);
            int filled = barWidth * value.Percent / 100;
            Write(canvas, frame.Bounds, 2, "[" + new string('█', filled) + new string('·', barWidth - filled) + "]");
            Write(canvas, frame.Bounds, 4, "No network is used; progress is deterministic.");
        }
        public UiInteractionFragment BuildInteractionFragment(ICompositeDialogContentFrame frame, int focusOrder) => UiInteractionFragment.Empty;
        public CompositeDialogContentInputResult RouteInput(ConsoleInputEvent input, ICompositeDialogContentFrame frame, UiInputRouteContext route) => CompositeDialogContentInputResult.NotHandled;
        public void ApplyCommittedFrame(ICompositeDialogContentFrame frame) { }
        private static void Write(IUiCanvas canvas, Rect bounds, int row, string text)
        {
            if (row < bounds.Height) canvas.Write(bounds.X, bounds.Y + row, ConsoleTextMetrics.FitToCells(text, bounds.Width), DialogStyles.Fill);
        }
        private sealed record Frame(Rect Bounds) : ICompositeDialogContentFrame;
    }
}
