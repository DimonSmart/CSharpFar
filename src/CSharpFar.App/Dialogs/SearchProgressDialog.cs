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
    private static readonly UiTargetId ListTarget = new("search-progress.results");
    private static readonly UiTargetId ScrollbarTarget = new("search-progress.results.scrollbar");
    private static readonly UiTargetId GoToTarget = new("search-progress.goto");
    private static readonly UiTargetId StopTarget = new("search-progress.stop");

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogHost _modalDialogs;
    private readonly ISearchService _searchService;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public SearchProgressDialog(
        ModalDialogHost modalDialogs,
        ISearchService searchService,
        ConsolePalette? palette = null)
    {
        _modalDialogs = modalDialogs;
        _screen = modalDialogs.Screen;
        _searchService = searchService;
    }

    public SearchRunResult Show(SearchRequest request)
    {
        using var cts = new CancellationTokenSource();
        var syncRoot = new object();
        var results = new List<SearchResultItem>();
        SearchProgress latestProgress = new() { CurrentPath = request.RootPath };
        bool searchCompleted = false;
        bool searchCancelled = false;
        Exception? completedException = null;
        bool userCancelled = false;
        bool stopRequested = false;
        bool discardResults = false;
        SearchResultItem? goToResult = null;

        var list = new ScrollableList<SearchResultItem>(Array.Empty<SearchResultItem>(), item => FormatResult(item, DialogWidth))
        {
            EmptyText = "No files found yet",
            NormalStyle = FarDialogStyles.Fill,
            SelectedStyle = FarDialogStyles.Input,
            EmptyStyle = FarDialogStyles.Fill,
        };
        var state = new SearchProgressViewState(
            latestProgress,
            Array.Empty<SearchResultItem>(),
            SearchProgressStatus.Running);
        int focusedButton = 0;
        int committedListRows = 1;

        var progress = new LockedProgress<SearchProgress>(p =>
        {
            lock (syncRoot)
                latestProgress = p;
        });
        Task searchTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in _searchService.SearchAsync(request, progress, cts.Token)
                    .ConfigureAwait(false))
                {
                    lock (syncRoot)
                        results.Add(item);
                }
            }
            catch (OperationCanceledException)
            {
                lock (syncRoot)
                    searchCancelled = true;
            }
            catch (Exception ex)
            {
                lock (syncRoot)
                    completedException = ex;
            }
            finally
            {
                lock (syncRoot)
                    searchCompleted = true;
            }
        });

        SearchDialogCompletion completion = _modalDialogs.RunInteractiveTimed<SearchProgressFrame, SearchProgressInput, SearchDialogCompletion>(
            (context, focusScope) => Render(context, focusScope, request, state, list, focusedButton, CanGoTo(), CanRequestStop()),
            BuildInteractionFrame,
            (input, frame, route) => RouteInput(input, frame, route, list, ref focusedButton, CanRequestStop()),
            (_, input) => HandleInput(input),
            getNextWakeUtc: GetNextWakeUtc,
            handleWake: HandleWake,
            prepareRender: () => SynchronizeVisibleState(ReadSnapshot()),
            applyCommittedFrame: frame =>
            {
                committedListRows = frame.ListState.ViewportRows;
                list.ApplyCommittedFrame(frame.ListState);
            });

        searchTask.GetAwaiter().GetResult();
        if (completion.Exception is not null)
            throw completion.Exception;
        return completion.Result ?? throw new InvalidOperationException("Search progress did not produce a result.");

        bool CanGoTo() => list.SelectedItemOrDefault is not null;

        bool CanRequestStop()
        {
            lock (syncRoot)
                return !stopRequested && !searchCompleted && goToResult is null;
        }

        DateTimeOffset? GetNextWakeUtc()
        {
            SearchProgressSnapshot snapshot = ReadSnapshot();
            return snapshot.Completed
                ? DateTimeOffset.UtcNow
                : DateTimeOffset.UtcNow.AddMilliseconds(RedrawDelayMilliseconds);
        }

        ModalDialogWakeResult<SearchDialogCompletion> HandleWake(SearchProgressFrame frame)
        {
            SearchProgressSnapshot snapshot = ReadSnapshot();
            bool changed = SynchronizeVisibleState(snapshot);
            if (!snapshot.Completed)
                return changed ? ModalDialogWakeResult<SearchDialogCompletion>.Changed : ModalDialogWakeResult<SearchDialogCompletion>.NoChange;

            SearchDialogCompletion final = BuildCompletion(snapshot);
            return ModalDialogWakeResult<SearchDialogCompletion>.Complete(final, invalidate: changed);
        }

        ModalDialogLoopResult<SearchDialogCompletion> HandleInput(SearchProgressInput input)
        {
            switch (input.Kind)
            {
                case SearchProgressInputKind.GoTo:
                    if (input.Result is { } selected)
                    {
                        goToResult = selected;
                        userCancelled = true;
                        cts.Cancel();
                    }
                    return ModalDialogLoopResult<SearchDialogCompletion>.Continue;
                case SearchProgressInputKind.Stop:
                    if (!CanRequestStop())
                        return ModalDialogLoopResult<SearchDialogCompletion>.Continue;

                    if (ConfirmStopSearch())
                    {
                        stopRequested = true;
                        userCancelled = true;
                        discardResults = true;
                        cts.Cancel();
                    }
                    return ModalDialogLoopResult<SearchDialogCompletion>.Continue;
                default:
                    return ModalDialogLoopResult<SearchDialogCompletion>.Continue;
            }
        }

        SearchDialogCompletion BuildCompletion(SearchProgressSnapshot snapshot)
        {
            if (snapshot.Exception is not null)
                return new SearchDialogCompletion(null, snapshot.Exception);

            IReadOnlyList<SearchResultItem> finalResults = discardResults ? [] : snapshot.Results;
            return new SearchDialogCompletion(
                new SearchRunResult(
                    finalResults,
                    snapshot.Cancelled || userCancelled || discardResults || goToResult is not null,
                    goToResult,
                    discardResults),
                null);
        }

        bool SynchronizeVisibleState(SearchProgressSnapshot snapshot)
        {
            SearchProgressStatus status = snapshot.Completed
                ? snapshot.Exception is null ? SearchProgressStatus.Completed : SearchProgressStatus.Failed
                : stopRequested || goToResult is not null ? SearchProgressStatus.Stopping : SearchProgressStatus.Running;
            var next = new SearchProgressViewState(snapshot.Progress, snapshot.Results, status);
            bool changed = HasVisibleChanges(state, next);
            state = next;
            list.ReplaceItems(next.Results, static item => new SearchResultKey(item.FullPath, item.Kind), committedListRows);
            return changed;
        }

        SearchProgressSnapshot ReadSnapshot()
        {
            lock (syncRoot)
            {
                return new SearchProgressSnapshot(
                    latestProgress,
                    [.. results],
                    searchCompleted,
                    searchCancelled,
                    completedException);
            }
        }
    }

    private bool ConfirmStopSearch() =>
        new OperationCancelDialog(_modalDialogs).Show(
            "Search has been interrupted",
            "Do you really want to stop it?");

    private SearchProgressFrame Render(
        UiRenderContext context,
        UiFocusScope focusScope,
        SearchRequest request,
        SearchProgressViewState state,
        ScrollableList<SearchResultItem> list,
        int focusedButton,
        bool canGoTo,
        bool canStop)
    {
        SearchProgressLayout? resultLayout = null;
        DialogButtonBarLayout? buttonLayout = null;
        DialogButtonBar? buttonBar = null;
        ScrollableListFrameState listState = ScrollableListFrameState.Empty;
        var outerBounds = _modalRenderer.CenteredOuterBounds(
            context.Size,
            DialogWidth,
            DialogHeight,
            minWidth: 50,
            minHeight: 14);

        _modalRenderer.Render(
            _screen,
            outerBounds,
            $"Find file: {request.FileMaskExpression}",
            true,
            FarDialogStyles.OuterOptions,
            FarDialogStyles.FrameOptions,
            (_, layout) =>
            {
                Rect bounds = layout.FrameBounds;
                int contentX = bounds.X + 2;
                int contentWidth = Math.Max(1, bounds.Width - 4);

                _screen.Write(contentX, bounds.Y + 1, ShortenMiddle(state.Progress.CurrentPath ?? request.RootPath, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);
                _screen.Write(contentX, bounds.Y + 2, StatsLine(state.Progress, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);

                string errorText = state.Progress.LastErrorMessage is null
                    ? StatusText(state.Status)
                    : ShortenMiddle($"{state.Progress.LastErrorPath}: {state.Progress.LastErrorMessage}", contentWidth);
                _screen.Write(contentX, bounds.Y + 3, errorText.PadRight(contentWidth), state.Status == SearchProgressStatus.Failed ? FarDialogStyles.Error : FarDialogStyles.Fill);

                DrawSeparator(bounds, bounds.Y + 4);

                int listY = bounds.Y + 5;
                int listHeight = VisibleResultRows(bounds);
                Rect listBounds = new(contentX, listY, contentWidth, listHeight);
                Rect scrollbarBounds = new(bounds.Right - 1, listY, 1, listHeight);
                listState = list.CalculateFrameState(listHeight, list.Count > listHeight ? scrollbarBounds : null);
                list.Render(_screen, listBounds, listState);
                if (list.GetScrollState(listHeight, listState.ScrollTop) is { } scrollState)
                {
                    new ScrollBarRenderer().RenderVerticalScrollbar(
                        _screen,
                        scrollbarBounds,
                        scrollState,
                        new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false },
                        FarDialogStyles.Border);
                }

                buttonBar = CreateButtonBar(canGoTo, canStop);
                focusedButton = Math.Clamp(focusedButton, 0, buttonBar.Count - 1);
                buttonLayout = buttonBar.Render(
                    _screen,
                    contentX,
                    bounds.Y + bounds.Height - 2,
                    contentWidth,
                    focusedButton,
                    FarDialogStyles.Fill,
                    FarDialogStyles.FocusedInput);
                resultLayout = new SearchProgressLayout(bounds, listBounds, scrollbarBounds, listHeight);
            });

        return new SearchProgressFrame(
            resultLayout ?? throw new InvalidOperationException("Search progress layout was not rendered."),
            listState,
            buttonLayout ?? throw new InvalidOperationException("Search progress buttons were not rendered."),
            buttonBar ?? throw new InvalidOperationException("Search progress button bar was not rendered."),
            state.Results,
            canGoTo,
            canStop);
    }

    private static UiInteractionFrame BuildInteractionFrame(SearchProgressFrame frame)
    {
        var hits = new List<UiHitRegion> { new(ListTarget, frame.Layout.ListBounds) };
        if (frame.ListState.ScrollbarBounds is { } scrollbar)
            hits.Add(new UiHitRegion(ScrollbarTarget, scrollbar));
        if (frame.CanGoTo && frame.Buttons.ButtonBounds.Count > 0)
            hits.Add(new UiHitRegion(GoToTarget, frame.Buttons.ButtonBounds[0]));
        if (frame.CanStop && frame.Buttons.ButtonBounds.Count > 1)
            hits.Add(new UiHitRegion(StopTarget, frame.Buttons.ButtonBounds[1]));

        return new UiInteractionFrame(
            hits,
            new UiFocusFrame([new UiFocusEntry(ListTarget, 0)], ListTarget),
            ListTarget);
    }

    private static (SearchProgressInput Semantic, UiInputResult UiResult) RouteInput(
        ConsoleInputEvent input,
        SearchProgressFrame frame,
        UiInputRouteContext route,
        ScrollableList<SearchResultItem> list,
        ref int focusedButton,
        bool canStop)
    {
        list.ApplyCommittedFrame(frame.ListState);
        if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape })
            return (SearchProgressInput.Stop, UiInputResult.HandledResult);

        if (frame.ButtonBar.TryHandleInput(input, frame.Buttons, ref focusedButton, out string? buttonId))
        {
            if (buttonId == StopButton && canStop)
                return (SearchProgressInput.Stop, UiInputResult.HandledAndInvalidate);

            if (buttonId == GoToButton && frame.SelectedResult is { } selected)
                return (SearchProgressInput.GoTo(selected), UiInputResult.HandledAndInvalidate);

            return (SearchProgressInput.None, UiInputResult.HandledAndInvalidate);
        }

        ScrollableListInputResult listInput = input switch
        {
            KeyConsoleInputEvent { Key: var key } => list.HandleKey(key, frame.ListState.ViewportRows),
            MouseConsoleInputEvent mouse => list.HandleMouse(
                mouse,
                frame.Layout.ListBounds,
                frame.ListState.ScrollbarBounds,
                frame.ListState.ViewportRows),
            _ => ScrollableListInputResult.NotHandled,
        };

        if (!listInput.IsHandled)
            return (SearchProgressInput.None, UiInputResult.NotHandled);

        if (listInput.Kind == ScrollableListInputResultKind.Confirmed && frame.SelectedResult is { } confirmed)
            return (SearchProgressInput.GoTo(confirmed), UiInputResult.HandledAndInvalidate);

        if (listInput.DragStarted)
            return (SearchProgressInput.None, UiInputResult.CaptureMouse(ScrollbarTarget, MouseButton.Left, invalidate: true));
        if (listInput.DragEnded)
            return (SearchProgressInput.None, UiInputResult.ReleaseMouse(invalidate: true));

        return (SearchProgressInput.None, UiInputResult.HandledAndInvalidate);
    }

    private static DialogButtonBar CreateButtonBar(bool canGoTo, bool canStop) =>
        new(
        [
            new DialogButton(GoToButton, "Go to", 'G', IsDefault: true, IsEnabled: canGoTo),
            new DialogButton(StopButton, "Stop", 'S', IsEnabled: canStop),
        ]);

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

    private void DrawSeparator(Rect bounds, int y)
    {
        if (y <= bounds.Y || y >= bounds.Bottom - 1)
            return;

        _screen.WriteChar(bounds.X, y, '╟', FarDialogStyles.Border);
        _screen.Write(bounds.X + 1, y, new string('─', Math.Max(0, bounds.Width - 2)), FarDialogStyles.Border);
        _screen.WriteChar(bounds.Right - 1, y, '╢', FarDialogStyles.Border);
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
        SearchResultItem[] Results,
        bool Completed,
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
        Rect ListBounds,
        Rect ScrollbarBounds,
        int VisibleResultRows);

    private sealed record SearchProgressFrame(
        SearchProgressLayout Layout,
        ScrollableListFrameState ListState,
        DialogButtonBarLayout Buttons,
        DialogButtonBar ButtonBar,
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
