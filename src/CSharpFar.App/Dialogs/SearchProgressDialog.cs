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
        {
            using var cts = new CancellationTokenSource();
            var syncRoot = new object();
            var results = new List<SearchResultItem>();
            SearchProgress latestProgress = new() { CurrentPath = request.RootPath };
            bool cancelled = false;
            bool userCancelled = false;
            bool stopRequested = false;
            bool discardResults = false;
            Exception? completedException = null;
            SearchResultItem? goToResult = null;
            int selectedIndex = 0;
            int scrollOffset = 0;
            ScrollBarDragState? resultScrollbarDrag = null;
            int focusedButton = 0;
            var buttonBar = new DialogButtonBar(
            [
                new DialogButton(GoToButton, "Go to", 'G', IsDefault: true),
                new DialogButton(StopButton, "Stop", 'S'),
            ]);
            SearchProgress renderProgress = latestProgress;
            SearchResultItem[] renderResults = [];
            using var modal = _modalDialogs.Open(context =>
            {
                int frameSelectedIndex = selectedIndex;
                int frameScrollOffset = scrollOffset;
                NormalizeSelection(
                    renderResults.Length,
                    Math.Max(1, context.Size.Height - 10),
                    ref frameSelectedIndex,
                    ref frameScrollOffset);
                var drawResult = Draw(
                    context,
                    request,
                    renderProgress,
                    renderResults,
                    frameSelectedIndex,
                    frameScrollOffset,
                    buttonBar,
                    focusedButton);
                var frameLayout = drawResult.Layout;
                NormalizeSelection(
                    renderResults.Length,
                    frameLayout.VisibleResultRows,
                    ref frameSelectedIndex,
                    ref frameScrollOffset);
                return new SearchProgressFrame(frameLayout, frameSelectedIndex, frameScrollOffset, drawResult.Buttons);
            });

            var progress = new Progress<SearchProgress>(p =>
            {
                lock (syncRoot)
                    latestProgress = p;
            });
            Task task = Task.Run(async () =>
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
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    completedException = ex;
                }
            });

            while (!task.IsCompleted && !discardResults && goToResult is null)
            {
                SearchProgress progressSnapshot;
                SearchResultItem[] resultSnapshot;
                lock (syncRoot)
                {
                    progressSnapshot = latestProgress;
                    resultSnapshot = [.. results];
                }
                renderProgress = progressSnapshot;
                renderResults = resultSnapshot;

                var frame = modal.Render();
                ApplyFrame(frame);
                if (task.IsCompleted)
                    break;

                if (task.Wait(RedrawDelayMilliseconds))
                    break;

                SearchResultItem[] inputResults;
                lock (syncRoot)
                    inputResults = [.. results];
                if (task.IsCompleted)
                    break;

                bool hasInput = modal.TryReadInput(out var input, out frame);
                ApplyFrame(frame);
                if (hasInput && input is { } semanticInput)
                {
                    if (semanticInput is MouseConsoleInputEvent mouse &&
                        TryHandleResultScrollbarMouse(
                            mouse,
                            inputResults.Length,
                            frame,
                            ref selectedIndex,
                            ref scrollOffset,
                            ref resultScrollbarDrag))
                    {
                        continue;
                    }

                    SearchResultItem? selected = HandleInput(
                        semanticInput,
                        inputResults,
                        frame,
                        ref selectedIndex,
                        ref scrollOffset,
                        buttonBar,
                        ref focusedButton,
                        ref stopRequested);

                    if (selected is not null)
                    {
                        goToResult = selected;
                        cancelled = true;
                        userCancelled = true;
                        cts.Cancel();
                    }
                }

                if (stopRequested)
                {
                    if (ConfirmStopSearch())
                    {
                        cancelled = true;
                        userCancelled = true;
                        discardResults = true;
                        cts.Cancel();
                    }
                    else
                        stopRequested = false;
                }

            }

            void ApplyFrame(SearchProgressFrame frame)
            {
                selectedIndex = frame.SelectedIndex;
                scrollOffset = frame.ScrollOffset;
            }

            try { task.Wait(2000); }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                cancelled = true;
            }

            if (completedException is not null)
                throw completedException;

            lock (syncRoot)
            {
                IReadOnlyList<SearchResultItem> finalResults = discardResults ? [] : [.. results];
                return new SearchRunResult(
                    finalResults,
                    cancelled || userCancelled || discardResults || goToResult is not null,
                    goToResult,
                    discardResults);
            }
        }
    }

    private bool ConfirmStopSearch() =>
        new OperationCancelDialog(_modalDialogs).Show(
            "Search has been interrupted",
            "Do you really want to stop it?");

    private SearchResultItem? HandleInput(
        ConsoleInputEvent input,
        IReadOnlyList<SearchResultItem> results,
        SearchProgressFrame frame,
        ref int selectedIndex,
        ref int scrollOffset,
        DialogButtonBar buttonBar,
        ref int focusedButton,
        ref bool stopRequested)
    {
        int listHeight = frame.Layout.VisibleResultRows;
        if (buttonBar.TryHandleInput(input, frame.Buttons, ref focusedButton, out string? buttonId))
        {
            if (buttonId == StopButton)
            {
                stopRequested = true;
                return null;
            }

            if (buttonId == GoToButton && results.Count > 0)
                return results[selectedIndex];

            return null;
        }

        if (input is not KeyConsoleInputEvent { Key: var key })
            return null;

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                stopRequested = true;
                break;
            case ConsoleKey.UpArrow:
                MoveSelection(-1, results.Count, listHeight, ref selectedIndex, ref scrollOffset);
                break;
            case ConsoleKey.DownArrow:
                MoveSelection(+1, results.Count, listHeight, ref selectedIndex, ref scrollOffset);
                break;
            case ConsoleKey.PageUp:
                MoveSelection(-listHeight, results.Count, listHeight, ref selectedIndex, ref scrollOffset);
                break;
            case ConsoleKey.PageDown:
                MoveSelection(+listHeight, results.Count, listHeight, ref selectedIndex, ref scrollOffset);
                break;
            case ConsoleKey.Home:
                if (results.Count > 0)
                {
                    selectedIndex = 0;
                    scrollOffset = 0;
                }
                break;
            case ConsoleKey.End:
                if (results.Count > 0)
                {
                    selectedIndex = results.Count - 1;
                    EnsureSelectedVisible(listHeight, ref selectedIndex, ref scrollOffset);
                }
                break;
        }

        return null;
    }

    private SearchProgressDrawResult Draw(
        UiRenderContext context,
        SearchRequest request,
        SearchProgress progress,
        IReadOnlyList<SearchResultItem> results,
        int selectedIndex,
        int scrollOffset,
        DialogButtonBar buttonBar,
        int focusedButton)
    {
        SearchProgressLayout? resultLayout = null;
        DialogButtonBarLayout? buttons = null;
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

                _screen.Write(contentX, bounds.Y + 1, ShortenMiddle(progress.CurrentPath ?? request.RootPath, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);
                _screen.Write(contentX, bounds.Y + 2, StatsLine(progress, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);

                string errorText = progress.LastErrorMessage is null
                    ? string.Empty
                    : ShortenMiddle($"{progress.LastErrorPath}: {progress.LastErrorMessage}", contentWidth);
                _screen.Write(contentX, bounds.Y + 3, errorText.PadRight(contentWidth), FarDialogStyles.Error);

                DrawSeparator(bounds, bounds.Y + 4);
                resultLayout = DrawResults(bounds, contentX, contentWidth, results, selectedIndex, scrollOffset);

                buttons = buttonBar.Render(
                    _screen,
                    contentX,
                    bounds.Y + bounds.Height - 2,
                    contentWidth,
                    focusedButton,
                    FarDialogStyles.Fill,
                    FarDialogStyles.FocusedInput);
            });
        return new SearchProgressDrawResult(
            resultLayout ?? throw new InvalidOperationException("Search progress layout was not rendered."),
            buttons ?? throw new InvalidOperationException("Search progress buttons were not rendered."));
    }

    private SearchProgressLayout DrawResults(
        Rect frameBounds,
        int contentX,
        int contentWidth,
        IReadOnlyList<SearchResultItem> results,
        int selectedIndex,
        int scrollOffset)
    {
        int listY = frameBounds.Y + 5;
        int listHeight = VisibleResultRows(frameBounds);

        if (results.Count == 0)
        {
            _screen.Write(contentX, listY, "No files found yet".PadRight(contentWidth), FarDialogStyles.Fill);
            for (int row = 1; row < listHeight; row++)
                _screen.Write(contentX, listY + row, new string(' ', contentWidth), FarDialogStyles.Fill);
            return new SearchProgressLayout(frameBounds, new Rect(frameBounds.Right - 1, listY, 1, listHeight), listHeight);
        }

        for (int row = 0; row < listHeight; row++)
        {
            int resultIndex = scrollOffset + row;
            string text = resultIndex < results.Count
                ? FormatResult(results[resultIndex], contentWidth)
                : string.Empty;
            var style = resultIndex == selectedIndex ? FarDialogStyles.Input : FarDialogStyles.Fill;
            _screen.Write(contentX, listY + row, text.PadRight(contentWidth), style);
        }

        var scrollbarBounds = new Rect(frameBounds.Right - 1, listY, 1, listHeight);
        if (results.Count > listHeight)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                _screen,
                scrollbarBounds,
                new ScrollState
                {
                    TotalItems = results.Count,
                    ViewportItems = listHeight,
                    FirstVisibleIndex = scrollOffset,
                },
                new ScrollBarOptions
                {
                    Enabled = true,
                    DrawWhenNotScrollable = false,
                },
                FarDialogStyles.Border);
        }

        return new SearchProgressLayout(frameBounds, scrollbarBounds, listHeight);
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

    private bool TryHandleResultScrollbarMouse(
        MouseConsoleInputEvent mouse,
        int resultCount,
        SearchProgressFrame frame,
        ref int selectedIndex,
        ref int scrollOffset,
        ref ScrollBarDragState? dragState)
    {
        var layout = frame.Layout;
        if (layout is null || resultCount <= layout.VisibleResultRows)
            return false;

        selectedIndex = frame.SelectedIndex;
        scrollOffset = frame.ScrollOffset;
        return ScrollableListMouseHandler.TryHandleScrollbarMouse(
            mouse,
            layout.ScrollbarBounds,
            resultCount,
            layout.VisibleResultRows,
            ref selectedIndex,
            ref scrollOffset,
            ref dragState);
    }

    private static void MoveSelection(
        int delta,
        int resultCount,
        int listHeight,
        ref int selectedIndex,
        ref int scrollOffset) =>
        ScrollStateCalculator.MoveSelection(delta, resultCount, listHeight, ref selectedIndex, ref scrollOffset);

    private static void NormalizeSelection(
        int resultCount,
        int listHeight,
        ref int selectedIndex,
        ref int scrollOffset) =>
        ScrollStateCalculator.NormalizeSelection(resultCount, listHeight, ref selectedIndex, ref scrollOffset);

    private static void EnsureSelectedVisible(
        int listHeight,
        ref int selectedIndex,
        ref int scrollOffset) =>
        scrollOffset = ScrollStateCalculator.EnsureIndexVisible(selectedIndex, scrollOffset, listHeight);

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

    private sealed record SearchProgressLayout(Rect FrameBounds, Rect ScrollbarBounds, int VisibleResultRows);

    private sealed record SearchProgressDrawResult(
        SearchProgressLayout Layout,
        DialogButtonBarLayout Buttons);

    private sealed record SearchProgressFrame(
        SearchProgressLayout Layout,
        int SelectedIndex,
        int ScrollOffset,
        DialogButtonBarLayout Buttons);
}
