using CSharpFar.App.Commands;
using CSharpFar.App.Panels;
using CSharpFar.Console;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Module.Abstractions;
using CSharpFar.Plugin.Abstractions;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Modules;

internal sealed class ModulePanelOpener
{
    private readonly NativeModuleCatalog _moduleCatalog;
    private readonly PanelNavigationService? _navigation;
    private readonly FilePanelSourceRegistry _sourceRegistry;
    private readonly PanelController _controller;
    private readonly Action<string, string> _showMessage;
    private readonly PluginPanelSourceCleanup _cleanup;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Func<PanelSide, FilePanelState> _getPanelState;
    private readonly Action<PanelSide> _setActiveSide;
    private readonly Action<bool> _setQuickView;
    private readonly Action<Action>? _postToUi;
    private readonly Action? _drainUiActions;
    private readonly Action<Exception>? _reportOperationError;
    private readonly CancellationTokenSource _operationCancellation = new();
    private readonly object _operationsGate = new();
    private readonly HashSet<Task> _activeOperations = [];
    private int _shuttingDown;
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(2);

    public ModulePanelOpener(
        NativeModuleCatalog moduleCatalog,
        FilePanelSourceRegistry sourceRegistry,
        PanelController controller,
        ScreenRenderer screen,
        DialogService dialogs,
        Func<CSharpFarPalette> palette,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Func<PanelSide, FilePanelState> getPanelState,
        Action<PanelSide> setActiveSide,
        Action<bool> setQuickView,
        Action<Action>? postToUi = null,
        Action<Exception>? reportCleanupError = null,
        Action<string, string>? showMessage = null,
        PanelNavigationService? navigation = null,
        Action? drainUiActions = null,
        Action<Exception>? reportOperationError = null)
    {
        _moduleCatalog = moduleCatalog;
        _navigation = navigation;
        _sourceRegistry = sourceRegistry;
        _controller = controller;
        _showMessage = showMessage ?? (dialogs ?? throw new ArgumentNullException(nameof(dialogs))).Message;
        _cleanup = new PluginPanelSourceCleanup(reportCleanupError);
        _panelOptions = panelOptions;
        _getPanelState = getPanelState;
        _setActiveSide = setActiveSide;
        _setQuickView = setQuickView;
        _postToUi = postToUi;
        _drainUiActions = drainUiActions;
        _reportOperationError = reportOperationError;
    }

    public ApplicationCommandResult OpenMenuItem(Guid actionId, PanelSide activeSide) =>
        HandleInvocation(_moduleCatalog.OpenFromMenu(actionId, activeSide, _operationCancellation.Token), activeSide);

    public ApplicationCommandResult OpenDiskMenuItem(Guid actionId, PanelSide panelSide) =>
        HandleInvocation(_moduleCatalog.OpenFromDiskMenu(actionId, panelSide, _operationCancellation.Token), panelSide);

    public bool TryOpenFromCommandLine(string command, PanelSide activeSide)
    {
        if (!_moduleCatalog.TryOpenFromCommandLine(command, activeSide, out var task, _operationCancellation.Token))
            return false;

        HandleInvocation(task, activeSide);
        return true;
    }

    private ApplicationCommandResult HandleInvocation(
        ValueTask<PluginCommandResult> invocation, PanelSide targetSide)
    {
        if (invocation.IsCompleted)
            return HandleOpenResult(invocation.Result, targetSide);

        if (_postToUi is null)
        {
            _showMessage("Plugin", "Asynchronous plugin commands require a UI completion queue.");
            TrackOperation(DiscardCommandResultAsync(invocation));
            return ApplicationCommandResult.Rendered();
        }

        TrackOperation(CompleteAsync(invocation, targetSide));
        return ApplicationCommandResult.Rendered();
    }

    private async Task CompleteAsync(ValueTask<PluginCommandResult> invocation, PanelSide targetSide)
    {
        PluginCommandResult result;
        try
        {
            result = await invocation.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = PluginCommandResult.Cancelled();
        }
        catch (Exception e)
        {
            result = PluginCommandResult.Failed(e.Message);
        }

        PostCompletion(() => HandleOpenResult(result, targetSide), result.Panel);
    }

    public void OpenPanel(PanelSide panelSide, IModulePanel panel) =>
        OpenPanel(panelSide, ModulePluginBridge.ToPanelRequest(panel));

    public void OpenPanel(
        PanelSide panelSide, PluginPanelOpenRequest request, FilePanelItem? entryItem = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.Source;
        var state = _getPanelState(panelSide);
        var returnPoint = entryItem is null ? null : _navigation?.CaptureReturnPoint(state, entryItem);
        bool newlyRegistered = false;

        if (_sourceRegistry.TryGetSource(source.SourceId, out var existing))
        {
            if (!ReferenceEquals(source, existing))
            {
                bool reusableSource = false;
                try
                {
                    reusableSource = existing is IReusableFilePanelSource reusable && reusable.CanReuse(source);
                }
                catch (Exception error)
                {
                    ReportOperationError(new IOException("Plugin panel source reuse check failed.", error));
                }
                if (reusableSource)
                {
                    _cleanup.Discard(source);
                    source = existing;
                }
                else
                {
                    _cleanup.Discard(source);
                    _showMessage("Plugin", "This panel source is already in use with different connection settings.");
                    return;
                }
            }
        }
        else
        {
            newlyRegistered = _sourceRegistry.TryAddOwned(source);
            if (!newlyRegistered)
            {
                _cleanup.Discard(source);
                _showMessage("Plugin", "Unable to register plugin panel source.");
                return;
            }
        }

        bool loaded = _controller.TryLoadLocation(
            state,
            new PanelLocation(source.SourceId, request.InitialDirectory),
            _panelOptions(),
            preserveOnError: true);
        if (!loaded)
        {
            if (newlyRegistered && _sourceRegistry.RemoveIfSame(source.SourceId, source))
                _cleanup.Discard(source);
            _showMessage("Plugin", "Unable to open plugin panel. The existing panel was not changed.");
            return;
        }

        // A previously nested or shared source may have a pending retirement.
        // It is visible now, before any old navigation leases are released.
        _sourceRegistry.RetainSource(source.SourceId);
        if (returnPoint is not null)
            _navigation!.EnterNestedSource(state, panelSide, returnPoint);
        else
            _navigation?.ClearNavigation(panelSide);

        _controller.SetSourceDisplay(state, request.Title, showCurrentItemFullPath: true);
        try
        {
            // Persist opt-in connection settings only after initial panel loading succeeded.
            (source as IFilePanelSourceActivation)?.OnPanelOpened();
        }
        catch (Exception ex)
        {
            _showMessage("Plugin", $"Panel opened, but connection settings could not be saved: {ex.Message}");
        }
        _setQuickView(false);
        _setActiveSide(panelSide);
    }

    /// <summary>Handles Enter for plugin-owned file types without blocking the UI input loop.</summary>
    public bool TryOpenFileItem(
        FilePanelState state, PanelSide side, FilePanelItem item,
        Action<FilePanelState, FilePanelItem> fallback)
    {
        if (!_sourceRegistry.TryGetSource(item.SourceId, out var source) ||
            !_moduleCatalog.TryOpenFile(source, item, side, out var invocation, _operationCancellation.Token))
            return false;

        var origin = state.CurrentLocation;
        if (invocation.IsCompleted)
        {
            CompleteFileOpen(invocation.Result, state, side, item, origin, fallback);
            return true;
        }

        if (_postToUi is null)
        {
            _showMessage("Open file", "Asynchronous file handlers require a UI completion queue.");
            TrackOperation(DiscardFileResultAsync(invocation));
            return true;
        }
        TrackOperation(AwaitFileOpenAsync(invocation, state, side, item, origin, fallback));
        return true;
    }

    private async Task AwaitFileOpenAsync(
        ValueTask<PluginFileOpenResult> invocation,
        FilePanelState state, PanelSide side, FilePanelItem item, PanelLocation origin,
        Action<FilePanelState, FilePanelItem> fallback)
    {
        PluginFileOpenResult result;
        try { result = await invocation.ConfigureAwait(false); }
        catch (OperationCanceledException) { result = PluginFileOpenResult.Cancelled(); }
        catch (Exception error) { result = PluginFileOpenResult.Failed(error.Message); }
        PostCompletion(() => CompleteFileOpen(result, state, side, item, origin, fallback), result.Panel);
    }

    private void CompleteFileOpen(
        PluginFileOpenResult result,
        FilePanelState state, PanelSide side, FilePanelItem item, PanelLocation origin,
        Action<FilePanelState, FilePanelItem> fallback)
    {
        if (state.CurrentLocation != origin)
        {
            DiscardUnusedPanel(result.Panel);
            return;
        }

        switch (result.Kind)
        {
            case PluginFileOpenResultKind.NotHandled:
                fallback(state, item);
                break;
            case PluginFileOpenResultKind.OpenPanel:
                OpenPanel(side, result.Panel!, item);
                break;
            case PluginFileOpenResultKind.Failed:
                _showMessage("Open file", result.Error ?? "File handler failed.");
                break;
        }
    }

    public ApplicationCommandResult HandleOpenResult(
        PluginCommandResult result, PanelSide targetSide)
    {
        switch (result.Kind)
        {
            case PluginCommandResultKind.OpenPanel:
                OpenPanel(targetSide, result.Panel!);
                break;
            case PluginCommandResultKind.Failed:
                _showMessage("Plugin", result.Error ?? "Plugin operation failed.");
                break;
        }
        return ApplicationCommandResult.Rendered();
    }

    private void TrackOperation(Task operation)
    {
        lock (_operationsGate)
            _activeOperations.Add(operation);
        _ = ObserveOperationAsync(operation);
    }

    private async Task ObserveOperationAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch (Exception error) { ReportOperationError(error); }
        finally
        {
            lock (_operationsGate)
                _activeOperations.Remove(operation);
        }
    }

    private async Task DiscardCommandResultAsync(ValueTask<PluginCommandResult> invocation)
    {
        try { DiscardUnusedPanel((await invocation.ConfigureAwait(false)).Panel); }
        catch (Exception error) { ReportOperationError(error); }
    }

    private async Task DiscardFileResultAsync(ValueTask<PluginFileOpenResult> invocation)
    {
        try { DiscardUnusedPanel((await invocation.ConfigureAwait(false)).Panel); }
        catch (Exception error) { ReportOperationError(error); }
    }

    private void PostCompletion(Action action, PluginPanelOpenRequest? panel)
    {
        try
        {
            // Synchronize enqueueing with shutdown's final drain.
            lock (_operationsGate)
            {
                if (_shuttingDown == 0)
                {
                    _postToUi!(() =>
                    {
                        if (Volatile.Read(ref _shuttingDown) != 0)
                            DiscardUnusedPanel(panel);
                        else
                            action();
                    });
                    return;
                }
            }
            DiscardUnusedPanel(panel);
        }
        catch (Exception error)
        {
            DiscardUnusedPanel(panel);
            ReportOperationError(error);
        }
    }

    private void DiscardUnusedPanel(PluginPanelOpenRequest? panel)
    {
        if (panel?.Source is not { } source)
            return;
        if (!_sourceRegistry.TryGetSource(source.SourceId, out var registered) ||
            !ReferenceEquals(registered, source))
            _cleanup.Discard(source);
    }

    private void ReportOperationError(Exception error)
    {
        try { _reportOperationError?.Invoke(error); }
        catch { /* Diagnostics cannot interfere with shutdown. */ }
    }

    /// <summary>Retire temporary sources after neither panel needs them and operations have finished.</summary>
    public void CleanupUnusedTemporarySources() =>
        _sourceRegistry.RemoveUnusedTemporarySources(
            _getPanelState(PanelSide.Left).SourceId,
            _getPanelState(PanelSide.Right).SourceId,
            _cleanup.Discard);

    /// <summary>
    /// Called after the terminal input loop ends. Drop saved navigation leases,
    /// retire every owned panel source, and await asynchronous cleanup.
    /// </summary>
    public async Task ShutdownAsync()
    {
        bool firstShutdown;
        Task[] running;
        lock (_operationsGate)
        {
            firstShutdown = _shuttingDown == 0;
            _shuttingDown = 1;
            running = [.. _activeOperations];
        }
        if (firstShutdown)
        {
            // Do not execute arbitrary plugin cancellation callbacks on the UI thread.
            Task cancellation = _operationCancellation.CancelAsync();

            // In-process code may ignore cancellation; shutdown must remain bounded.
            try
            {
                await Task.WhenAll(running.Append(cancellation))
                    .WaitAsync(ShutdownGracePeriod).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                ReportOperationError(new TimeoutException(
                    "Plugin operations did not stop within the shutdown grace period."));
            }
            catch (Exception error) { ReportOperationError(error); }
        }

        // Callbacks posted before shutdown are abandoned, not executed against dead panels.
        _drainUiActions?.Invoke();
        _navigation?.ClearNavigation(PanelSide.Left);
        _navigation?.ClearNavigation(PanelSide.Right);
        _sourceRegistry.DisposeOwnedSources(_cleanup.Discard);
        await _cleanup.AwaitPendingAsync().ConfigureAwait(false);
    }

    /// <summary>Finish rejected-source cleanup after the input loop stops.</summary>
    public Task AwaitPendingCleanupsAsync() => _cleanup.AwaitPendingAsync();
}
