using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

internal sealed class SearchProgressSession
{
    private SearchProgressTerminalIntent _terminalIntent;

    public bool CanGoTo => _terminalIntent == SearchProgressTerminalIntent.None;

    public bool CanStop => _terminalIntent == SearchProgressTerminalIntent.None;

    public bool IsStopping => _terminalIntent != SearchProgressTerminalIntent.None;

    public SearchResultItem? GoToResult { get; private set; }

    public bool TryGoTo(SearchResultItem result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!CanGoTo)
            return false;

        GoToResult = result;
        _terminalIntent = SearchProgressTerminalIntent.GoTo;
        return true;
    }

    public bool TryStop()
    {
        if (!CanStop)
            return false;

        _terminalIntent = SearchProgressTerminalIntent.StopAndDiscard;
        return true;
    }

    public SearchRunResult BuildResult(IReadOnlyList<SearchResultItem> results, bool cancelled) =>
        _terminalIntent switch
        {
            SearchProgressTerminalIntent.StopAndDiscard => new SearchRunResult([], Cancelled: true, DiscardResults: true),
            SearchProgressTerminalIntent.GoTo => new SearchRunResult(results, Cancelled: true, GoToResult),
            _ => new SearchRunResult(results, Cancelled: cancelled),
        };

    private enum SearchProgressTerminalIntent
    {
        None,
        StopAndDiscard,
        GoTo,
    }
}
