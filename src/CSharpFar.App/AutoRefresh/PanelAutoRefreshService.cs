using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.AutoRefresh;

internal sealed class PanelAutoRefreshService
{
    private readonly IFileSystemChangeWatcher? _watcher;
    private readonly PanelController _controller;
    private readonly IFileSystemLocationService? _locationService;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Func<PanelSide, FilePanelState> _getPanelState;
    private readonly Func<PanelSide, int> _visibleRows;
    private readonly Action<FilePanelState, int> _safeRefresh;
    private readonly Dictionary<PanelSide, FileSystemPanelChanged> _pendingRefreshEvents = new();
    private readonly object _waitGate = new();
    private CancellationTokenSource _refreshCts = new();

    public PanelAutoRefreshService(
        IFileSystemChangeWatcher? watcher,
        PanelController controller,
        IFileSystemLocationService? locationService,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Func<PanelSide, FilePanelState> getPanelState,
        Func<PanelSide, int> visibleRows,
        Action<FilePanelState, int> safeRefresh)
    {
        _watcher = watcher;
        _controller = controller;
        _locationService = locationService;
        _panelOptions = panelOptions;
        _getPanelState = getPanelState;
        _visibleRows = visibleRows;
        _safeRefresh = safeRefresh;

        if (_watcher != null)
            _watcher.Changed += OnFileSystemChanged;
    }

    public CancellationToken WaitToken { get { lock (_waitGate) return _refreshCts.Token; } }

    public void StartWatching(FilePanelState state, PanelSide side)
    {
        if (_watcher == null || _locationService == null) return;
        if (state.SourceId != PanelSourceId.Local ||
            !HasCapability(state, PanelProviderCapabilities.Watch))
        {
            _controller.SetAutoRefreshState(state, null);
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
        _controller.SetAutoRefreshState(state, refreshState);
    }

    public void ResetWaitToken()
    {
        lock (_waitGate)
        {
            var old = _refreshCts;
            _refreshCts = new CancellationTokenSource();
            old.Dispose();
        }
    }

    public void WakeInputLoop()
    {
        lock (_waitGate) _refreshCts.Cancel();
    }

    public void ProcessPendingRefreshes()
    {
        FileSystemPanelChanged[] events;
        lock (_pendingRefreshEvents)
        {
            events = [.. _pendingRefreshEvents.Values];
            _pendingRefreshEvents.Clear();
        }
        foreach (var evt in events)
        {
            var state = _getPanelState(evt.PanelSide);
            if (!string.Equals(state.CurrentDirectory, evt.DirectoryPath, StringComparison.OrdinalIgnoreCase))
                continue;

            _safeRefresh(state, _visibleRows(evt.PanelSide));
        }
    }

    private void OnFileSystemChanged(object? sender, FileSystemPanelChanged e)
    {
        lock (_pendingRefreshEvents)
            _pendingRefreshEvents[e.PanelSide] = e;

        WakeInputLoop();
    }

    private static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;
}
