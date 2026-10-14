using CSharpFar.App.Commands;
using CSharpFar.Console;
using CSharpFar.Core.Controllers;
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
    private readonly FilePanelSourceRegistry _sourceRegistry;
    private readonly PanelController _controller;
    private readonly DialogService _dialogs;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Func<PanelSide, FilePanelState> _getPanelState;
    private readonly Action<PanelSide> _setActiveSide;
    private readonly Action<bool> _setQuickView;
    private readonly Action<Action>? _postToUi;

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
        Action<Action>? postToUi = null)
    {
        _moduleCatalog = moduleCatalog;
        _sourceRegistry = sourceRegistry;
        _controller = controller;
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _panelOptions = panelOptions;
        _getPanelState = getPanelState;
        _setActiveSide = setActiveSide;
        _setQuickView = setQuickView;
        _postToUi = postToUi;
    }

    public ApplicationCommandResult OpenMenuItem(Guid actionId, PanelSide activeSide) =>
        HandleInvocation(_moduleCatalog.OpenFromMenu(actionId, activeSide), activeSide);

    public ApplicationCommandResult OpenDiskMenuItem(Guid actionId, PanelSide panelSide) =>
        HandleInvocation(_moduleCatalog.OpenFromDiskMenu(actionId, panelSide), panelSide);

    public bool TryOpenFromCommandLine(string command, PanelSide activeSide)
    {
        if (!_moduleCatalog.TryOpenFromCommandLine(command, activeSide, out var task))
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
            _dialogs.Message("Plugin", "Asynchronous plugin commands require a UI completion queue.");
            return ApplicationCommandResult.Rendered();
        }

        _ = CompleteAsync(invocation, targetSide);
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

        _postToUi!(() => HandleOpenResult(result, targetSide));
    }

    public void OpenPanel(PanelSide panelSide, IModulePanel panel) =>
        OpenPanel(panelSide, ModulePluginBridge.ToPanelRequest(panel));

    public void OpenPanel(PanelSide panelSide, PluginPanelOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.Source;
        var state = _getPanelState(panelSide);
        bool newlyRegistered = false;

        if (_sourceRegistry.TryGetSource(source.SourceId, out var existing))
        {
            if (!ReferenceEquals(source, existing))
            {
                DisposeUnused(source);
                _dialogs.Message("Plugin", "This panel source is already registered. The existing panel was not changed.");
                return;
            }
        }
        else
        {
            newlyRegistered = _sourceRegistry.TryAdd(source);
            if (!newlyRegistered)
            {
                DisposeUnused(source);
                _dialogs.Message("Plugin", "Unable to register plugin panel source.");
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
            if (newlyRegistered)
            {
                _sourceRegistry.RemoveIfSame(source.SourceId, source);
                DisposeUnused(source);
            }
            _dialogs.Message("Plugin", "Unable to open plugin panel. The existing panel was not changed.");
            return;
        }

        _controller.SetSourceDisplay(state, request.Title, showCurrentItemFullPath: true);
        _setQuickView(false);
        _setActiveSide(panelSide);
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
                _dialogs.Message("Plugin", result.Error ?? "Plugin operation failed.");
                break;
        }
        return ApplicationCommandResult.Rendered();
    }

    private static void DisposeUnused(CSharpFar.Core.Abstractions.IFilePanelSource source)
    {
        if (source is IAsyncDisposable asyncDisposable)
        {
            var dispose = asyncDisposable.DisposeAsync();
            if (!dispose.IsCompleted)
                throw new InvalidOperationException("Asynchronous source disposal requires an async lifecycle.");
            dispose.GetAwaiter().GetResult();
        }
        else if (source is IDisposable disposable)
            disposable.Dispose();
    }
}
