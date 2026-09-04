using CSharpFar.Console;
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

    public SearchProgressDialog(ModalDialogHost _, ISearchService searchService, DialogService dialogs, CSharpFarPalette? palette = null)
    {
        _searchService = searchService;
        _dialogs = dialogs;
    }

    public SearchRunResult Show(SearchRequest request)
    {
        var syncRoot = new object();
        var results = new List<SearchResultItem>();
        SearchProgress latestProgress = new() { CurrentPath = request.RootPath };
        var session = new SearchProgressSession();
        var table = new TableList<SearchResultItem>([], new TableListDefinition<SearchResultItem>
        {
            Columns = [TableColumn<SearchResultItem>.Text("Results", item => FormatResult(item), TableWidth.Flexible(70, 12))],
        });
        var buttons = FormControls.Buttons(CreateButtons(false, true));
        var form = new ScrollableFormDialog();
        SearchProgressViewState state = new(latestProgress, [], SearchProgressStatus.Running);

        void SetRows()
        {
            form.SetRows(
            [
                FormControls.Label(ShortenMiddle(state.Progress.CurrentPath ?? request.RootPath, 70)),
                FormControls.Label(StatsLine(state.Progress, 70)),
                FormControls.Label(state.Progress.LastErrorMessage is null ? StatusText(state.Status) : state.Progress.LastErrorMessage),
            ],
            [buttons]);
        }

        SetRows();
        return _dialogs.Operation(
            new OperationDialogOptions(new CompositeDialogOptions($"Find file: {request.FileMaskExpression}", 76, 18, 50, 14), TimeSpan.FromMilliseconds(60)),
            RunSearchAsync,
            form,
            table,
            status: null,
            commands: null,
            synchronize: Synchronize,
            handle: Handle,
            complete: Complete);

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

        bool Synchronize()
        {
            SearchProgressSnapshot snapshot;
            lock (syncRoot) snapshot = new(latestProgress, [.. results]);
            var next = new SearchProgressViewState(snapshot.Progress, snapshot.Results, session.IsStopping ? SearchProgressStatus.Stopping : SearchProgressStatus.Running);
            bool changed = !Equals(state, next);
            state = next;
            table.ReplaceItems(next.Results, static item => new SearchResultKey(item.FullPath, item.Kind));
            buttons.SetButtons(CreateButtons(session.CanGoTo && table.HasItems, session.CanStop));
            SetRows();
            return changed;
        }

        OperationDialogOutcome<SearchRunResult> Handle(CompositeDialogEvent @event)
        {
            if (@event.Kind == CompositeDialogEventKind.ContentConfirmed && table.TryGetSelectedItem(out SearchResultItem selected) && session.TryGoTo(selected))
                return OperationDialogOutcome<SearchRunResult>.RequestCancellation;

            if (@event.Kind == CompositeDialogEventKind.Command && @event.Command == GoToButton && table.TryGetSelectedItem(out selected) && session.TryGoTo(selected))
                return OperationDialogOutcome<SearchRunResult>.RequestCancellation;

            if ((@event.Kind == CompositeDialogEventKind.Command && @event.Command == StopButton) || @event.Kind == CompositeDialogEventKind.Cancelled)
            {
                if (session.CanStop && ConfirmStopSearch() && session.TryStop())
                    return OperationDialogOutcome<SearchRunResult>.RequestCancellation;
            }
            return OperationDialogOutcome<SearchRunResult>.ContinueNoChange;
        }

        SearchRunResult Complete(SearchBackgroundOutcome outcome)
        {
            state = new(outcome.FinalProgress, outcome.Results, SearchProgressStatus.Completed);
            table.ReplaceItems(outcome.Results, static item => new SearchResultKey(item.FullPath, item.Kind));
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
