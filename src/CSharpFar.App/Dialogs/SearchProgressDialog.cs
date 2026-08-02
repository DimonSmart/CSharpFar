using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record SearchRunResult(
    IReadOnlyList<SearchResultItem> Results,
    bool Cancelled,
    SearchResultItem? GoToResult = null,
    bool DiscardResults = false);

internal sealed class SearchProgressDialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 18;
    private const int RedrawDelayMilliseconds = 60;
    private const string GoToButton = "goto";
    private const string StopButton = "stop";

    private readonly ModalDialogHost _modalDialogs;
    private readonly ISearchService _searchService;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public SearchProgressDialog(
        ModalDialogHost modalDialogs,
        ISearchService searchService,
        ConsolePalette? palette = null)
    {
        _modalDialogs = modalDialogs;
        _searchService = searchService;
    }

    public SearchRunResult Show(SearchRequest request)
    {
        using var cts = new CancellationTokenSource();
        var syncRoot = new object();
        var results = new List<SearchResultItem>();
        SearchProgress latestProgress = new() { CurrentPath = request.RootPath };
        var session = new SearchProgressSession(cts);

        var targets = new UiTargetScope("search-progress");
        var resultState = new ScrollableListState<SearchResultItem>([]);
        var routedList = new RoutedScrollableList<SearchResultItem>(resultState, targets.Child("results"), targets.Child("results.scrollbar"));
        var presentation = new ScrollableListRenderOptions<SearchResultItem>(item => FormatResult(item, DialogWidth), "No files found yet", FarDialogStyles.Fill, FarDialogStyles.Input, FarDialogStyles.Fill);
        var state = new SearchProgressViewState(
            latestProgress,
            Array.Empty<SearchResultItem>(),
            SearchProgressStatus.Running);
        var buttons = new ButtonRow(CreateButtons(canGoTo: false, canStop: true)) { Id = "actions" };
        var form = new ScrollableFormDialog();
        form.SetRows([], [buttons]);
        int committedListRows = 1;

        var progress = new LockedProgress<SearchProgress>(p =>
        {
            lock (syncRoot)
                latestProgress = p;
        });
        Task<SearchBackgroundOutcome> searchTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in _searchService.SearchAsync(request, progress, cts.Token)
                    .ConfigureAwait(false))
                {
                    lock (syncRoot)
                        results.Add(item);
                }

                return CreateBackgroundOutcome(cancelled: false, exception: null);
            }
            catch (OperationCanceledException)
            {
                return CreateBackgroundOutcome(cancelled: true, exception: null);
            }
            catch (Exception ex)
            {
                return CreateBackgroundOutcome(cancelled: false, exception: ex);
            }
        });
        var completionWake = new CancellationTokenSource();
        _ = searchTask.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Cancel(),
            completionWake,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        SearchDialogCompletion completion;
        try
        {
            completion = _modalDialogs.RunInteractiveTimed<SearchProgressFrame, SearchProgressInput, SearchDialogCompletion>(
                (context, focusScope) => Render(context, focusScope, request, state, routedList, presentation, form, CanGoTo(), CanRequestStop()),
                frame => BuildInteractionFrame(frame, routedList),
                (input, frame, route) => RouteInput(input, frame, route, routedList, form),
                (_, input) => HandleInput(input),
                getNextWakeUtc: GetNextWakeUtc,
                handleWake: HandleWake,
                prepareRender: () =>
                {
                    SynchronizeVisibleState(ReadStreamingSnapshot());
                    buttons.SetButtons(CreateButtons(CanGoTo(), CanRequestStop()));
                },
                applyCommittedFrame: frame => committedListRows = frame.List.ViewportRows,
                wakeSignal: completionWake.Token);
        }
        catch (Exception)
        {
            TryCancel(cts);
            ObserveSearchTaskAfterUiException(searchTask);
            throw;
        }

        if (completion.Exception is not null)
            throw completion.Exception;
        return completion.Result ?? throw new InvalidOperationException("Search progress did not produce a result.");

        bool CanGoTo() => session.CanGoTo && resultState.TryGetSelectedItem(out _) && !searchTask.IsCompleted;

        bool CanRequestStop()
            => session.CanStop && !searchTask.IsCompleted;

        DateTimeOffset? GetNextWakeUtc() => DateTimeOffset.UtcNow.AddMilliseconds(RedrawDelayMilliseconds);

        ModalDialogWakeResult<SearchDialogCompletion> HandleWake(SearchProgressFrame frame)
        {
            if (!searchTask.IsCompleted)
            {
                bool streamingChanged = SynchronizeVisibleState(ReadStreamingSnapshot());
                return streamingChanged ? ModalDialogWakeResult<SearchDialogCompletion>.Changed : ModalDialogWakeResult<SearchDialogCompletion>.NoChange;
            }

            SearchBackgroundOutcome outcome = searchTask.GetAwaiter().GetResult();
            bool finalChanged = SynchronizeVisibleStateFromOutcome(outcome);
            SearchDialogCompletion final = BuildCompletion(outcome);
            return ModalDialogWakeResult<SearchDialogCompletion>.Complete(final, invalidate: finalChanged);
        }

        ModalDialogLoopResult<SearchDialogCompletion> HandleInput(SearchProgressInput input)
        {
            switch (input.Kind)
            {
                case SearchProgressInputKind.GoTo:
                    if (!searchTask.IsCompleted && input.Result is { } selected && session.TryGoTo(selected))
                        return ModalDialogLoopResult<SearchDialogCompletion>.ContinueChanged;
                    return ModalDialogLoopResult<SearchDialogCompletion>.ContinueNoChange;
                case SearchProgressInputKind.Stop:
                    if (!CanRequestStop())
                        return ModalDialogLoopResult<SearchDialogCompletion>.ContinueNoChange;

                    if (ConfirmStopSearch())
                    {
                        return session.TryStop()
                            ? ModalDialogLoopResult<SearchDialogCompletion>.ContinueChanged
                            : ModalDialogLoopResult<SearchDialogCompletion>.ContinueNoChange;
                    }
                    return ModalDialogLoopResult<SearchDialogCompletion>.ContinueNoChange;
                default:
                    return ModalDialogLoopResult<SearchDialogCompletion>.ContinueNoChange;
            }
        }

        SearchDialogCompletion BuildCompletion(SearchBackgroundOutcome outcome)
        {
            if (outcome.Exception is not null)
                return new SearchDialogCompletion(null, outcome.Exception);

            return new SearchDialogCompletion(session.BuildResult(outcome.Results, outcome.Cancelled), null);
        }

        bool SynchronizeVisibleState(SearchProgressSnapshot snapshot)
        {
            SearchProgressStatus status = session.IsStopping ? SearchProgressStatus.Stopping : SearchProgressStatus.Running;
            var next = new SearchProgressViewState(snapshot.Progress, snapshot.Results, status);
            bool changed = HasVisibleChanges(state, next);
            state = next;
            resultState.ReplaceItems(next.Results, static item => new SearchResultKey(item.FullPath, item.Kind), committedListRows);
            return changed;
        }

        bool SynchronizeVisibleStateFromOutcome(SearchBackgroundOutcome outcome)
        {
            SearchProgressStatus status = outcome.Exception is null ? SearchProgressStatus.Completed : SearchProgressStatus.Failed;
            var next = new SearchProgressViewState(outcome.FinalProgress, outcome.Results, status);
            bool changed = HasVisibleChanges(state, next);
            state = next;
            resultState.ReplaceItems(next.Results, static item => new SearchResultKey(item.FullPath, item.Kind), committedListRows);
            return changed;
        }

        SearchProgressSnapshot ReadStreamingSnapshot()
        {
            lock (syncRoot)
            {
                return new SearchProgressSnapshot(
                    latestProgress,
                    [.. results]);
            }
        }

        SearchBackgroundOutcome CreateBackgroundOutcome(bool cancelled, Exception? exception)
        {
            lock (syncRoot)
                return new SearchBackgroundOutcome([.. results], latestProgress, cancelled, exception);
        }
    }

    private static void ObserveSearchTaskAfterUiException(Task<SearchBackgroundOutcome> searchTask)
    {
        _ = searchTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (Exception)
        {
        }
    }

    private bool ConfirmStopSearch() =>
        new OperationCancelDialog(_modalDialogs).Show(
            "Search has been interrupted",
            "Do you really want to stop it?");

    private SearchProgressFrame Render(
        UiRenderContext context,
        IUiFocusState focusScope,
        SearchRequest request,
        SearchProgressViewState state,
        RoutedScrollableList<SearchResultItem> list, ScrollableListRenderOptions<SearchResultItem> presentation,
        ScrollableFormDialog form,
        bool canGoTo,
        bool canStop)
    {
        ModalDialogRenderer.Layout modal = _modalRenderer.CalculateLayout(
            context.Size,
            DialogWidth,
            DialogHeight,
            minWidth: 50,
            minHeight: 14);
        SearchProgressLayout layout = CalculateLayout(modal, list.State.Count);
        ScrollableFormFrame? buttonFrame = null;
        ScrollableListFrame listFrame = list.CalculateFrame(layout.ListBounds, layout.ScrollbarBounds);
        _modalRenderer.Render(
            context.Canvas,
            modal,
            $"Find file: {request.FileMaskExpression}",
            true,
            FarDialogStyles.OuterOptions,
            FarDialogStyles.FrameOptions,
            (_, _) =>
            {
                if (layout.StatusBounds.Width > 0)
                {
                    int contentWidth = layout.StatusBounds.Width;
                    context.Canvas.Write(layout.StatusBounds.X, layout.StatusBounds.Y, ShortenMiddle(state.Progress.CurrentPath ?? request.RootPath, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);
                    if (layout.StatusBounds.Height > 1)
                        context.Canvas.Write(layout.StatusBounds.X, layout.StatusBounds.Y + 1, StatsLine(state.Progress, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);
                    if (layout.StatusBounds.Height > 2)
                    {
                        string errorText = state.Progress.LastErrorMessage is null
                            ? StatusText(state.Status)
                            : ShortenMiddle($"{state.Progress.LastErrorPath}: {state.Progress.LastErrorMessage}", contentWidth);
                        context.Canvas.Write(layout.StatusBounds.X, layout.StatusBounds.Y + 2, errorText.PadRight(contentWidth), state.Status == SearchProgressStatus.Failed ? FarDialogStyles.Error : FarDialogStyles.Fill);
                    }
                }

                DrawSeparator(context.Canvas, modal.FrameBounds, layout.SeparatorY);
                list.Render(context.Canvas, listFrame, presentation);
                list.RenderScrollbar(context.Canvas, listFrame, FarDialogStyles.Border);

                buttonFrame = layout.FooterBounds.Height > 0
                    ? form.Render(
                        new FormRenderContext(
                            context,
                            layout.FormBodyBounds,
                            FarDialogStyles.Border,
                            layout.FooterBounds),
                        focusScope,
                        [new UiFocusEntry(list.ListTarget, 0)],
                        list.ListTarget)
                    : EmptyFormFrame(context, layout.FormBodyBounds);
            });

        return new SearchProgressFrame(
            layout,
            listFrame,
            buttonFrame ?? throw new InvalidOperationException("Search progress buttons were not rendered."),
            form,
            state.Results,
            canGoTo,
            canStop);
    }

    private static SearchProgressLayout CalculateLayout(ModalDialogRenderer.Layout modal, int itemCount)
    {
        Rect frame = modal.FrameBounds;
        Rect statusBounds = UiLayout.Inset(frame, left: 2, top: 1, right: 2, bottom: 1);
        int separatorY = Math.Min(frame.Bottom, frame.Y + 4);
        int listY = Math.Min(frame.Bottom, frame.Y + 5);
        int footerY = Math.Max(listY, frame.Bottom - 2);
        Rect listBounds = new(statusBounds.X, listY, statusBounds.Width, Math.Max(0, footerY - listY - 1));
        Rect? scrollbarBounds = listBounds.Width > 0 && listBounds.Height > 0 && itemCount > listBounds.Height
            ? new Rect(frame.Right - 1, listBounds.Y, 1, listBounds.Height)
            : null;
        Rect formBodyBounds = new(statusBounds.X, Math.Clamp(listBounds.Bottom, frame.Y, frame.Bottom), statusBounds.Width, listBounds.Bottom < footerY ? 1 : 0);
        Rect footerBounds = new(statusBounds.X, footerY, statusBounds.Width, footerY < frame.Bottom ? 1 : 0);
        return new SearchProgressLayout(frame, statusBounds, separatorY, listBounds, scrollbarBounds, formBodyBounds, footerBounds);
    }

    private static ScrollableFormFrame EmptyFormFrame(UiRenderContext context, Rect bodyBounds) =>
        new(context.Viewport, bodyBounds, null, 0, context.Viewport.Height, 0, [], null);

    private static UiInteractionFrame BuildInteractionFrame(
        SearchProgressFrame frame,
        RoutedScrollableList<SearchResultItem> list)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddFragment(list.BuildInteractionFragment(
                frame.List,
                0,
                frame.Layout.ListBounds.Width > 0 && frame.Layout.ListBounds.Height > 0))
            .SetDefaultFocusTarget(frame.Layout.ListBounds.Width > 0 && frame.Layout.ListBounds.Height > 0 ? list.ListTarget : null);
        if (frame.Layout.ListBounds.Width > 0 && frame.Layout.ListBounds.Height > 0)
            builder.SetKeyboardTarget(list.ListTarget);
        builder.AddFragment(frame.Form.BuildInteractionFragment(frame.Buttons));

        return builder.Build();
    }

    private static (SearchProgressInput Semantic, UiInputResult UiResult) RouteInput(
        ConsoleInputEvent input,
        SearchProgressFrame frame,
        UiInputRouteContext route,
        RoutedScrollableList<SearchResultItem> list,
        ScrollableFormDialog form)
    {
        if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape })
            return frame.CanStop
                ? (SearchProgressInput.Stop, UiInputResult.HandledResult)
                : (SearchProgressInput.None, UiInputResult.HandledResult);

        bool isListRoute = list.IsTargetRoute(route);
        if (!isListRoute)
        {
            FormRouteResult formResult = form.RouteInput(input, frame.Buttons, route, allowUnfocusedButtonHotkeys: true);
            return (ButtonInput(frame, formResult.FormResult.Command), formResult.UiResult);
        }

        if (input is KeyConsoleInputEvent { Key.KeyChar: > ' ' } keyInput)
        {
            FormRouteResult formResult = form.RouteInput(keyInput, frame.Buttons, route, allowUnfocusedButtonHotkeys: true);
            if (formResult.FormResult.IsHandled)
                return (ButtonInput(frame, formResult.FormResult.Command), formResult.UiResult);
        }

        if (!frame.CanGoTo)
        {
            return UiFocusRouting.TryHandleTraversal(input, out UiInputResult focusResult)
                ? (SearchProgressInput.None, focusResult)
                : (SearchProgressInput.None, UiInputResult.HandledAndInvalidate);
        }

        RoutedScrollableListInputResult routedResult = list.RouteInput(input, frame.List, route);
        ScrollableListInputResult listInput = routedResult.ListResult;

        if (!listInput.IsHandled)
            return UiFocusRouting.TryHandleTraversal(input, out UiInputResult focusResult)
                ? (SearchProgressInput.None, focusResult)
                : (SearchProgressInput.None, UiInputResult.NotHandled);

        if (listInput.Kind == ScrollableListInputResultKind.Confirmed &&
            frame.CanGoTo &&
            list.State.SelectedIndex >= 0 &&
            list.State.SelectedIndex < frame.Results.Length)
        {
            SearchResultItem confirmed = frame.Results[list.State.SelectedIndex];
            return (SearchProgressInput.GoTo(confirmed), UiInputResult.HandledAndInvalidate);
        }

        return (SearchProgressInput.None, routedResult.UiResult);
    }

    private static SearchProgressInput ButtonInput(SearchProgressFrame frame, string? buttonId) =>
        buttonId switch
        {
            StopButton when frame.CanStop => SearchProgressInput.Stop,
            GoToButton when frame.CanGoTo && frame.SelectedResult is { } selected => SearchProgressInput.GoTo(selected),
            _ => SearchProgressInput.None,
        };

    private static IReadOnlyList<DialogButton> CreateButtons(bool canGoTo, bool canStop) =>
        [
            new DialogButton(GoToButton, "Go to", 'G', IsDefault: true, IsEnabled: canGoTo),
            new DialogButton(StopButton, "Stop", 'S', IsEnabled: canStop),
        ];

    private static string StatusText(SearchProgressStatus status) => status switch
    {
        SearchProgressStatus.Stopping => "Stopping...",
        SearchProgressStatus.Completed => "Completed",
        SearchProgressStatus.Failed => "Search failed",
        _ => string.Empty,
    };

    private static bool HasVisibleChanges(SearchProgressViewState current, SearchProgressViewState next)
    {
        if (current.Status != next.Status || !current.Progress.Equals(next.Progress))
            return true;
        if (current.Results.Length != next.Results.Length)
            return true;

        for (int i = 0; i < current.Results.Length; i++)
        {
            if (!current.Results[i].Equals(next.Results[i]))
                return true;
        }

        return false;
    }

    private static void DrawSeparator(IUiCanvas canvas, Rect bounds, int y)
    {
        if (y <= bounds.Y || y >= bounds.Bottom - 1)
            return;

        canvas.WriteChar(bounds.X, y, '╟', FarDialogStyles.Border);
        canvas.Write(bounds.X + 1, y, new string('─', Math.Max(0, bounds.Width - 2)), FarDialogStyles.Border);
        canvas.WriteChar(bounds.Right - 1, y, '╢', FarDialogStyles.Border);
    }


    private static string StatsLine(SearchProgress progress, int width)
    {
        string text =
            $"Files: {FormatInteger(progress.ScannedFiles)}  " +
            $"Folders: {FormatInteger(progress.ScannedDirectories)}  " +
            $"Found: {FormatInteger(progress.MatchedItems)}  " +
            $"Errors: {FormatInteger(progress.ErrorCount)}";
        return Truncate(text, width);
    }

    private static string FormatResult(SearchResultItem item, int width)
    {
        string prefix = item.Kind == SearchResultItemKind.Directory ? "[Dir] " : "      ";
        return ShortenMiddle(prefix + item.FullPath, width);
    }

    private static string FormatInteger(long value) =>
        value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "~";
    }

    private static string ShortenMiddle(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        if (value.Length <= maxLength)
            return value;
        if (maxLength <= 1)
            return "~";

        int left = (maxLength - 1) / 2;
        int right = maxLength - left - 1;
        return value[..left] + "~" + value[^right..];
    }

    private sealed class LockedProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private readonly record struct SearchResultKey(string FullPath, SearchResultItemKind Kind);

    private readonly record struct SearchProgressSnapshot(
        SearchProgress Progress,
        SearchResultItem[] Results);

    private sealed record SearchBackgroundOutcome(
        SearchResultItem[] Results,
        SearchProgress FinalProgress,
        bool Cancelled,
        Exception? Exception);

    private readonly record struct SearchProgressViewState(
        SearchProgress Progress,
        SearchResultItem[] Results,
        SearchProgressStatus Status);

    private enum SearchProgressStatus
    {
        Running,
        Stopping,
        Completed,
        Failed,
    }

    private enum SearchProgressInputKind
    {
        None,
        Stop,
        GoTo,
    }

    private readonly record struct SearchProgressInput(SearchProgressInputKind Kind, SearchResultItem? Result = null)
    {
        public static SearchProgressInput None => new(SearchProgressInputKind.None);
        public static SearchProgressInput Stop => new(SearchProgressInputKind.Stop);
        public static SearchProgressInput GoTo(SearchResultItem result) => new(SearchProgressInputKind.GoTo, result);
    }

    private sealed record SearchDialogCompletion(SearchRunResult? Result, Exception? Exception);

    private sealed record SearchProgressLayout(
        Rect FrameBounds,
        Rect StatusBounds,
        int SeparatorY,
        Rect ListBounds,
        Rect? ScrollbarBounds,
        Rect FormBodyBounds,
        Rect FooterBounds);

    private sealed record SearchProgressFrame(
        SearchProgressLayout Layout,
        ScrollableListFrame List,
        ScrollableFormFrame Buttons,
        ScrollableFormDialog Form,
        SearchResultItem[] Results,
        bool CanGoTo,
        bool CanStop)
    {
        public SearchResultItem? SelectedResult =>
            List.SelectedIndex >= 0 && List.SelectedIndex < Results.Length
                ? Results[List.SelectedIndex]
                : null;
    }
}
