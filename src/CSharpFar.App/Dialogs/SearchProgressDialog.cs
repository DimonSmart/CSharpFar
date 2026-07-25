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
        SearchCompletionIntent completionIntent = new SearchCompletionIntent.None();

        var list = new ScrollableList<SearchResultItem>(Array.Empty<SearchResultItem>(), item => FormatResult(item, DialogWidth))
        {
            EmptyText = "No files found yet",
            NormalStyle = FarDialogStyles.Fill,
            SelectedStyle = FarDialogStyles.Input,
            EmptyStyle = FarDialogStyles.Fill,
        };
        var targets = new UiTargetScope("search-progress");
        var routedList = new RoutedScrollableList<SearchResultItem>(
            list,
            targets.Child("results"),
            targets.Child("results.scrollbar"));
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
                (context, focusScope) => Render(context, focusScope, request, state, routedList, form, CanGoTo(), CanRequestStop()),
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
                applyCommittedFrame: frame =>
                {
                    committedListRows = frame.ListState.ViewportRows;
                    routedList.ApplyCommittedFrame(frame.ListState);
                },
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

        bool CanGoTo() => completionIntent is SearchCompletionIntent.None && list.SelectedItemOrDefault is not null && !searchTask.IsCompleted;

        bool CanRequestStop()
            => completionIntent is SearchCompletionIntent.None && !searchTask.IsCompleted;

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
            if (completionIntent is not SearchCompletionIntent.None)
                return ModalDialogLoopResult<SearchDialogCompletion>.Continue;

            switch (input.Kind)
            {
                case SearchProgressInputKind.GoTo:
                    if (!searchTask.IsCompleted && input.Result is { } selected)
                    {
                        completionIntent = new SearchCompletionIntent.GoTo(selected);
                        cts.Cancel();
                    }
                    return ModalDialogLoopResult<SearchDialogCompletion>.Continue;
                case SearchProgressInputKind.Stop:
                    if (!CanRequestStop())
                        return ModalDialogLoopResult<SearchDialogCompletion>.Continue;

                    if (ConfirmStopSearch())
                    {
                        completionIntent = new SearchCompletionIntent.StopAndDiscard();
                        cts.Cancel();
                    }
                    return ModalDialogLoopResult<SearchDialogCompletion>.Continue;
                default:
                    return ModalDialogLoopResult<SearchDialogCompletion>.Continue;
            }
        }

        SearchDialogCompletion BuildCompletion(SearchBackgroundOutcome outcome)
        {
            if (outcome.Exception is not null)
                return new SearchDialogCompletion(null, outcome.Exception);

            SearchRunResult result = completionIntent switch
            {
                SearchCompletionIntent.StopAndDiscard => new SearchRunResult(
                    [],
                    Cancelled: true,
                    GoToResult: null,
                    DiscardResults: true),
                SearchCompletionIntent.GoTo goTo => new SearchRunResult(
                    outcome.Results,
                    Cancelled: true,
                    GoToResult: goTo.Result,
                    DiscardResults: false),
                SearchCompletionIntent.None => new SearchRunResult(
                    outcome.Results,
                    Cancelled: outcome.Cancelled,
                    GoToResult: null,
                    DiscardResults: false),
                _ => throw new InvalidOperationException($"Unsupported search completion intent: {completionIntent.GetType().Name}."),
            };
            return new SearchDialogCompletion(result, null);
        }

        bool SynchronizeVisibleState(SearchProgressSnapshot snapshot)
        {
            SearchProgressStatus status = completionIntent is SearchCompletionIntent.None
                ? SearchProgressStatus.Running
                : SearchProgressStatus.Stopping;
            var next = new SearchProgressViewState(snapshot.Progress, snapshot.Results, status);
            bool changed = HasVisibleChanges(state, next);
            state = next;
            list.ReplaceItems(next.Results, static item => new SearchResultKey(item.FullPath, item.Kind), committedListRows);
            return changed;
        }

        bool SynchronizeVisibleStateFromOutcome(SearchBackgroundOutcome outcome)
        {
            SearchProgressStatus status = outcome.Exception is null ? SearchProgressStatus.Completed : SearchProgressStatus.Failed;
            var next = new SearchProgressViewState(outcome.FinalProgress, outcome.Results, status);
            bool changed = HasVisibleChanges(state, next);
            state = next;
            list.ReplaceItems(next.Results, static item => new SearchResultKey(item.FullPath, item.Kind), committedListRows);
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
        RoutedScrollableList<SearchResultItem> list,
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
        SearchProgressLayout? resultLayout = null;
        ScrollableFormFrame? buttonFrame = null;
        ScrollableListFrameState listState = ScrollableListFrameState.Empty;
        _modalRenderer.Render(
            context.Canvas,
            modal.OuterBounds,
            $"Find file: {request.FileMaskExpression}",
            true,
            FarDialogStyles.OuterOptions,
            FarDialogStyles.FrameOptions,
            (_, _) =>
            {
                Rect bounds = modal.FrameBounds;
                int contentX = bounds.X + 2;
                int contentWidth = Math.Max(1, bounds.Width - 4);

                context.Canvas.Write(contentX, bounds.Y + 1, ShortenMiddle(state.Progress.CurrentPath ?? request.RootPath, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);
                context.Canvas.Write(contentX, bounds.Y + 2, StatsLine(state.Progress, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);

                string errorText = state.Progress.LastErrorMessage is null
                    ? StatusText(state.Status)
                    : ShortenMiddle($"{state.Progress.LastErrorPath}: {state.Progress.LastErrorMessage}", contentWidth);
                context.Canvas.Write(contentX, bounds.Y + 3, errorText.PadRight(contentWidth), state.Status == SearchProgressStatus.Failed ? FarDialogStyles.Error : FarDialogStyles.Fill);

                DrawSeparator(context.Canvas, bounds, bounds.Y + 4);

                int listY = bounds.Y + 5;
                int listHeight = VisibleResultRows(bounds);
                Rect listBounds = new(contentX, listY, contentWidth, listHeight);
                Rect scrollbarBounds = new(bounds.Right - 1, listY, 1, listHeight);
                listState = list.CalculateFrame(listHeight, list.Count > listHeight ? scrollbarBounds : null);
                list.Render(context.Canvas, listBounds, listState);
                if (list.GetScrollState(listHeight, listState.ScrollTop) is { } scrollState)
                {
                    new ScrollBarRenderer().RenderVerticalScrollbar(
                        context.Canvas,
                        scrollbarBounds,
                        scrollState,
                        new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false },
                        FarDialogStyles.Border);
                }

                buttonFrame = form.Render(
                    new FormRenderContext(
                        context,
                        new Rect(contentX, listBounds.Bottom, contentWidth, 1),
                        FarDialogStyles.Border,
                        new Rect(contentX, bounds.Y + bounds.Height - 2, contentWidth, 1)),
                    focusScope,
                    [new UiFocusEntry(list.ListTarget, 0)],
                    list.ListTarget);
                resultLayout = new SearchProgressLayout(bounds, listBounds, scrollbarBounds, listHeight);
            });

        return new SearchProgressFrame(
            resultLayout ?? throw new InvalidOperationException("Search progress layout was not rendered."),
            listState,
            buttonFrame ?? throw new InvalidOperationException("Search progress buttons were not rendered."),
            form,
            state.Results,
            canGoTo,
            canStop);
    }

    private static UiInteractionFrame BuildInteractionFrame(
        SearchProgressFrame frame,
        RoutedScrollableList<SearchResultItem> list)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddFragment(list.BuildInteractionFragment(frame.Layout.ListBounds, frame.ListState, 0))
            .SetDefaultFocusTarget(list.ListTarget)
            .SetKeyboardTarget(list.ListTarget);
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

        if (input is KeyConsoleInputEvent { Key: var focusKey } && TryRouteFocusKey(focusKey, frame, route, list.ListTarget, out UiInputResult focusResult))
            return (SearchProgressInput.None, focusResult);

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
            return (SearchProgressInput.None, UiInputResult.HandledAndInvalidate);

        RoutedScrollableListInputResult routedResult = list.RouteInput(input, frame.Layout.ListBounds, frame.ListState, route);
        ScrollableListInputResult listInput = routedResult.ListResult;

        if (!listInput.IsHandled)
            return (SearchProgressInput.None, UiInputResult.NotHandled);

        if (listInput.Kind == ScrollableListInputResultKind.Confirmed &&
            frame.CanGoTo &&
            list.SelectedIndex >= 0 &&
            list.SelectedIndex < frame.Results.Length)
        {
            SearchResultItem confirmed = frame.Results[list.SelectedIndex];
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

    private static bool TryRouteFocusKey(
        ConsoleKeyInfo key,
        SearchProgressFrame frame,
        UiInputRouteContext route,
        UiTargetId listTarget,
        out UiInputResult result)
    {
        if (key.Key != ConsoleKey.Tab)
        {
            result = UiInputResult.NotHandled;
            return false;
        }

        if (route.Target == listTarget && frame.Buttons.DefaultTarget is UiTargetId buttonTarget)
        {
            result = UiInputResult.RequestFocus(buttonTarget);
            return true;
        }

        result = UiInputResult.RequestFocus(listTarget);
        return true;
    }

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

    private static int VisibleResultRows(Rect frameBounds)
    {
        int listY = frameBounds.Y + 5;
        int buttonY = frameBounds.Y + frameBounds.Height - 2;
        return Math.Max(1, buttonY - listY - 1);
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

    private abstract record SearchCompletionIntent
    {
        public sealed record None : SearchCompletionIntent;

        public sealed record StopAndDiscard : SearchCompletionIntent;

        public sealed record GoTo(SearchResultItem Result) : SearchCompletionIntent;
    }

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
        Rect ListBounds,
        Rect ScrollbarBounds,
        int VisibleResultRows);

    private sealed record SearchProgressFrame(
        SearchProgressLayout Layout,
        ScrollableListFrameState ListState,
        ScrollableFormFrame Buttons,
        ScrollableFormDialog Form,
        SearchResultItem[] Results,
        bool CanGoTo,
        bool CanStop)
    {
        public SearchResultItem? SelectedResult =>
            ListState.SelectedIndex >= 0 && ListState.SelectedIndex < Results.Length
                ? Results[ListState.SelectedIndex]
                : null;
    }
}
