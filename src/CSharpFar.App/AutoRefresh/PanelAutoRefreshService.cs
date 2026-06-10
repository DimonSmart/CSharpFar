using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.AutoRefresh;

internal sealed class PanelAutoRefreshService
{
    private readonly IFileSystemChangeWatcher? _watcher;
    private readonly IFileSystemLocationService? _locationService;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Func<PanelSide, FilePanelState> _getPanelState;
    private readonly Func<PanelSide, int> _visibleRows;
    private readonly Action<FilePanelState, int> _safeRefresh;
    private readonly Queue<FileSystemPanelChanged> _pendingRefreshEvents = new();
    private CancellationTokenSource _refreshCts = new();

    public PanelAutoRefreshService(
        IFileSystemChangeWatcher? watcher,
        IFileSystemLocationService? locationService,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Func<PanelSide, FilePanelState> getPanelState,
        Func<PanelSide, int> visibleRows,
        Action<FilePanelState, int> safeRefresh)
    {
        _watcher = watcher;
        _locationService = locationService;
        _panelOptions = panelOptions;
        _getPanelState = getPanelState;
        _visibleRows = visibleRows;
        _safeRefresh = safeRefresh;

        if (_watcher != null)
            _watcher.Changed += OnFileSystemChanged;
    }

    public CancellationToken WaitToken => _refreshCts.Token;

    public void StartWatching(FilePanelState state, PanelSide side)
    {
        if (_watcher == null || _locationService == null) return;
        if (state.SourceId != PanelSourceId.Local ||
            !HasCapability(state, PanelProviderCapabilities.Watch))
        {
            state.AutoRefreshState = null;
            return;
        }

        var loc = _locationService.GetLocationInfo(state.CurrentDirectory);
        var req = new PanelWatchRequest
        {
            PanelSide = side,
            DirectoryPath = state.CurrentDirectory,
            ObjectCount = state.Items.Count,
            IsNetworkDrive = loc.IsNetworkDrive,
            Options = _panelOptions().AutoRefresh,
        };
        var refreshState = _watcher.StartWatching(req);
        state.AutoRefreshState = refreshState;
    }

    public void ResetWaitToken()
    {
        var old = Interlocked.Exchange(ref _refreshCts, new CancellationTokenSource());
        old.Dispose();
    }

    public void WakeInputLoop() =>
        _refreshCts.Cancel();

    public void ProcessPendingRefreshes()
    {
        while (true)
        {
            FileSystemPanelChanged? evt;
            lock (_pendingRefreshEvents)
            {
                evt = _pendingRefreshEvents.Count > 0 ? _pendingRefreshEvents.Dequeue() : null;
            }
            if (evt is null) break;

            var state = _getPanelState(evt.PanelSide);
            if (!string.Equals(state.CurrentDirectory, evt.DirectoryPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (Directory.Exists(state.CurrentDirectory))
                _safeRefresh(state, _visibleRows(evt.PanelSide));
        }
    }

    private void OnFileSystemChanged(object? sender, FileSystemPanelChanged e)
    {
        lock (_pendingRefreshEvents)
            _pendingRefreshEvents.Enqueue(e);

        WakeInputLoop();
    }

    private static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;
}
