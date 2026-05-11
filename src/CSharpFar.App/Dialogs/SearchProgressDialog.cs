using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

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
    private readonly ISearchService _searchService;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public SearchProgressDialog(
        ScreenRenderer screen,
        ISearchService searchService,
        ConsolePalette? palette = null)
    {
        _screen = screen;
        _searchService = searchService;
    }

    public SearchRunResult Show(SearchRequest request)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
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
            int focusedButton = 0;
            var buttonBar = new DialogButtonBar(
            [
                new DialogButton(GoToButton, "Go to", 'G', IsDefault: true),
                new DialogButton(StopButton, "Stop", 'S'),
            ]);

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

                int listHeight = VisibleResultRows();
                NormalizeSelection(resultSnapshot.Length, listHeight, ref selectedIndex, ref scrollOffset);
                Draw(request, progressSnapshot, resultSnapshot, selectedIndex, scrollOffset, buttonBar, focusedButton);

                Thread.Sleep(RedrawDelayMilliseconds);
                if (task.IsCompleted)
                    break;

                SearchResultItem[] inputResults;
                lock (syncRoot)
                    inputResults = [.. results];
                NormalizeSelection(inputResults.Length, listHeight, ref selectedIndex, ref scrollOffset);

                if (_screen.TryReadInput(out var input))
                {
                    SearchResultItem? selected = HandleInput(
                        input,
                        inputResults,
                        listHeight,
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
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private bool ConfirmStopSearch() =>
        new OperationCancelDialog(_screen).Show(
            "Search has been interrupted",
            "Do you really want to stop it?");

    private SearchResultItem? HandleInput(
        ConsoleInputEvent input,
        IReadOnlyList<SearchResultItem> results,
        int listHeight,
        ref int selectedIndex,
        ref int scrollOffset,
        DialogButtonBar buttonBar,
        ref int focusedButton,
        ref bool stopRequested)
    {
        if (buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
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

    private void Draw(
        SearchRequest request,
        SearchProgress progress,
        IReadOnlyList<SearchResultItem> results,
        int selectedIndex,
        int scrollOffset,
        DialogButtonBar buttonBar,
        int focusedButton)
    {
        using var frame = _screen.BeginFrame();

        var outerBounds = _modalRenderer.CenteredOuterBounds(
            _screen,
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
                DrawResults(bounds, contentX, contentWidth, results, selectedIndex, scrollOffset);

                buttonBar.Render(
                    _screen,
                    contentX,
                    bounds.Y + bounds.Height - 2,
                    contentWidth,
                    focusedButton,
                    FarDialogStyles.Fill,
                    FarDialogStyles.FocusedInput);
            });
    }

    private void DrawResults(
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
            return;
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
    }

    private void DrawSeparator(Rect bounds, int y)
    {
        if (y <= bounds.Y || y >= bounds.Bottom - 1)
            return;

        _screen.WriteChar(bounds.X, y, '╟', FarDialogStyles.Border);
        _screen.Write(bounds.X + 1, y, new string('─', Math.Max(0, bounds.Width - 2)), FarDialogStyles.Border);
        _screen.WriteChar(bounds.Right - 1, y, '╢', FarDialogStyles.Border);
    }

    private int VisibleResultRows()
    {
        var outerBounds = _modalRenderer.CenteredOuterBounds(
            _screen,
            DialogWidth,
            DialogHeight,
            minWidth: 50,
            minHeight: 14);
        var frameBounds = new Rect(
            outerBounds.X + 1,
            outerBounds.Y + 1,
            Math.Max(1, outerBounds.Width - 2),
            Math.Max(1, outerBounds.Height - 2));
        return VisibleResultRows(frameBounds);
    }

    private static int VisibleResultRows(Rect frameBounds)
    {
        int listY = frameBounds.Y + 5;
        int buttonY = frameBounds.Y + frameBounds.Height - 2;
        return Math.Max(1, buttonY - listY - 1);
    }

    private static void MoveSelection(
        int delta,
        int resultCount,
        int listHeight,
        ref int selectedIndex,
        ref int scrollOffset)
    {
        if (resultCount == 0)
            return;

        selectedIndex = Math.Clamp(selectedIndex + delta, 0, resultCount - 1);
        EnsureSelectedVisible(listHeight, ref selectedIndex, ref scrollOffset);
    }

    private static void NormalizeSelection(
        int resultCount,
        int listHeight,
        ref int selectedIndex,
        ref int scrollOffset)
    {
        if (resultCount == 0)
        {
            selectedIndex = 0;
            scrollOffset = 0;
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, resultCount - 1);
        int maxScroll = Math.Max(0, resultCount - listHeight);
        scrollOffset = Math.Clamp(scrollOffset, 0, maxScroll);
        EnsureSelectedVisible(listHeight, ref selectedIndex, ref scrollOffset);
    }

    private static void EnsureSelectedVisible(
        int listHeight,
        ref int selectedIndex,
        ref int scrollOffset)
    {
        if (selectedIndex < scrollOffset)
            scrollOffset = selectedIndex;
        else if (selectedIndex >= scrollOffset + listHeight)
            scrollOffset = selectedIndex - listHeight + 1;

        scrollOffset = Math.Max(0, scrollOffset);
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
}
