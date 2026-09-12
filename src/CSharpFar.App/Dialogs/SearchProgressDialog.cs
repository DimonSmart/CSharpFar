using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record SearchRunResult(IReadOnlyList<SearchResultItem> Results, bool Cancelled, SearchResultItem? GoToResult = null, bool DiscardResults = false);

internal sealed class SearchProgressDialog
{
    private const string GoToButton = "goto";
    private const string StopButton = "stop";
    private readonly DialogService _dialogs;
    private readonly ISearchService _searchService;

    public SearchProgressDialog(ISearchService searchService, DialogService dialogs)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public SearchRunResult Show(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var syncRoot = new object();
        var results = new List<SearchResultItem>();
        SearchProgress latestProgress = new() { CurrentPath = request.RootPath };
        var session = new SearchProgressSession();
        SearchProgressViewState state = new(latestProgress, [], SearchProgressStatus.Running);

        return _dialogs.Operation(new OperationDialogDefinition<SearchResultItem, SearchBackgroundOutcome, SearchRunResult>
        {
            Title = $"Find file: {request.FileMaskExpression}",
            PreferredWidth = 76,
            PreferredHeight = 18,
            MinWidth = 50,
            MinHeight = 14,
            RefreshInterval = TimeSpan.FromMilliseconds(60),
            TableDefinition = new TableListDefinition<SearchResultItem>
            {
                Columns = [TableColumn<SearchResultItem>.Text("Results", FormatResult, TableWidth.Flexible(70, 12))],
            },
            ItemIdentity = static item => new SearchResultKey(item.FullPath, item.Kind),
            Operation = RunSearchAsync,
            Synchronize = Synchronize,
            HandleItemActivation = Activate,
            HandleCommand = HandleCommand,
            HandleCancel = RequestStop,
            Complete = Complete,
        });

        async Task<SearchBackgroundOutcome> RunSearchAsync(CancellationToken cancellationToken)
        {
            var progress = new LockedProgress<SearchProgress>(value => { lock (syncRoot) latestProgress = value; });
            try
            {
                await foreach (SearchResultItem item in _searchService.SearchAsync(request, progress, cancellationToken).ConfigureAwait(false))
                {
                    lock (syncRoot) results.Add(item);
                }
                return Snapshot(cancelled: false);
            }
            catch (OperationCanceledException) { return Snapshot(cancelled: true); }

            SearchBackgroundOutcome Snapshot(bool cancelled)
            {
                lock (syncRoot) return new([.. results], latestProgress, cancelled);
            }
        }

        OperationDialogState<SearchResultItem> Synchronize()
        {
            SearchProgressSnapshot snapshot;
            lock (syncRoot) snapshot = new(latestProgress, [.. results]);

            state = new(
                snapshot.Progress,
                snapshot.Results,
                session.IsStopping ? SearchProgressStatus.Stopping : SearchProgressStatus.Running);

            return new OperationDialogState<SearchResultItem>(
                rows:
                [
                    FormControls.Label(ShortenMiddle(state.Progress.CurrentPath ?? request.RootPath, 70)),
                    FormControls.Label(StatsLine(state.Progress, 70)),
                    FormControls.Label(state.Progress.LastErrorMessage is null ? StatusText(state.Status) : state.Progress.LastErrorMessage),
                ],
                items: state.Results,
                buttons: CreateButtons(session.CanGoTo && state.Results.Length > 0, session.CanStop));
        }

        OperationDialogOutcome<SearchRunResult> Activate(SearchResultItem selected) =>
            session.TryGoTo(selected)
                ? OperationDialogOutcome<SearchRunResult>.RequestCancellation
                : OperationDialogOutcome<SearchRunResult>.ContinueNoChange;

        OperationDialogOutcome<SearchRunResult> HandleCommand(ListDialogActionContext<SearchResultItem> action)
        {
            if (action.ActionId == GoToButton && action.SelectedItem is { } selected)
                return Activate(selected);

            return action.ActionId == StopButton
                ? RequestStop()
                : OperationDialogOutcome<SearchRunResult>.ContinueNoChange;
        }

        OperationDialogOutcome<SearchRunResult> RequestStop()
        {
            if (session.CanStop && ConfirmStopSearch() && session.TryStop())
                return OperationDialogOutcome<SearchRunResult>.RequestCancellation;

            return OperationDialogOutcome<SearchRunResult>.ContinueNoChange;
        }

        SearchRunResult Complete(SearchBackgroundOutcome outcome)
        {
            state = new(outcome.FinalProgress, outcome.Results, SearchProgressStatus.Completed);
            return session.BuildResult(outcome.Results, outcome.Cancelled);
        }
    }

    private bool ConfirmStopSearch() => new OperationCancelDialog(_dialogs).Show("Search has been interrupted", "Do you really want to stop it?");
    private static IReadOnlyList<DialogButton> CreateButtons(bool canGoTo, bool canStop) => [new(GoToButton, "Go to", 'G', IsDefault: true, IsEnabled: canGoTo), new(StopButton, "Stop", 'S', IsEnabled: canStop)];
    private static string StatusText(SearchProgressStatus status) => status switch { SearchProgressStatus.Stopping => "Stopping...", SearchProgressStatus.Completed => "Completed", _ => string.Empty };
    private static string FormatResult(SearchResultItem item) => (item.Kind == SearchResultItemKind.Directory ? "[Dir] " : "      ") + item.FullPath;
    private static string StatsLine(SearchProgress progress, int _) => $"Files: {progress.ScannedFiles:N0}  Folders: {progress.ScannedDirectories:N0}  Found: {progress.MatchedItems:N0}  Errors: {progress.ErrorCount:N0}";
    private static string ShortenMiddle(string value, int maxLength) => value.Length <= maxLength ? value : value[..Math.Max(0, (maxLength - 1) / 2)] + "~" + value[^Math.Max(0, maxLength / 2)..];
    private sealed class LockedProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private readonly record struct SearchResultKey(string FullPath, SearchResultItemKind Kind);
    private readonly record struct SearchProgressSnapshot(SearchProgress Progress, SearchResultItem[] Results);
    private sealed record SearchBackgroundOutcome(SearchResultItem[] Results, SearchProgress FinalProgress, bool Cancelled);
    private readonly record struct SearchProgressViewState(SearchProgress Progress, SearchResultItem[] Results, SearchProgressStatus Status);
    private enum SearchProgressStatus { Running, Stopping, Completed }
}
