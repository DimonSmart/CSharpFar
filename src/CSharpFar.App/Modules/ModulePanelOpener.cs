using CSharpFar.App.Commands;
using CSharpFar.Console;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Module.Abstractions;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Modules;

internal sealed class ModulePanelOpener
{
    private readonly NativeModuleCatalog _moduleCatalog;
    private readonly FilePanelSourceRegistry _sourceRegistry;
    private readonly PanelController _controller;
    private readonly ScreenRenderer _screen;
    private readonly Func<ConsolePalette> _palette;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Func<PanelSide, FilePanelState> _getPanelState;
    private readonly Action<PanelSide> _setActiveSide;
    private readonly Action<bool> _setQuickView;

    public ModulePanelOpener(
        NativeModuleCatalog moduleCatalog,
        FilePanelSourceRegistry sourceRegistry,
        PanelController controller,
        ScreenRenderer screen,
        Func<ConsolePalette> palette,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Func<PanelSide, FilePanelState> getPanelState,
        Action<PanelSide> setActiveSide,
        Action<bool> setQuickView)
    {
        _moduleCatalog = moduleCatalog;
        _sourceRegistry = sourceRegistry;
        _controller = controller;
        _screen = screen;
        _palette = palette;
        _panelOptions = panelOptions;
        _getPanelState = getPanelState;
        _setActiveSide = setActiveSide;
        _setQuickView = setQuickView;
    }

    public ApplicationCommandResult OpenMenuItem(Guid actionId, PanelSide activeSide) =>
        HandleOpenResult(
            _moduleCatalog.OpenFromMenu(actionId, activeSide),
            activeSide);

    public ApplicationCommandResult OpenDiskMenuItem(Guid actionId, PanelSide panelSide) =>
        HandleOpenResult(
            _moduleCatalog.OpenFromDiskMenu(actionId, panelSide),
            panelSide);

    public bool TryOpenFromCommandLine(string command, PanelSide activeSide)
    {
        if (!_moduleCatalog.TryOpenFromCommandLine(command, activeSide, out var result))
            return false;

        HandleOpenResult(result, activeSide);
        return true;
    }

    public void OpenPanel(PanelSide panelSide, IModulePanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        _sourceRegistry.Add(panel);
        var panelInfo = panel.GetOpenPanelInfo();
        var state = _getPanelState(panelSide);
        _controller.TryLoadLocation(
            state,
            new PanelLocation(panel.SourceId, panelInfo.CurrentDirectory),
            _panelOptions());
        state.DisplayTitle = panelInfo.Title;
        state.ShowCurrentItemFullPath = true;
        _setQuickView(false);
        _setActiveSide(panelSide);
    }

    public ApplicationCommandResult HandleOpenResult(
        ModuleActionResult result,
        PanelSide defaultPanelSide)
    {
        switch (result.Kind)
        {
            case ModuleActionResultKind.OpenedPanel:
                OpenPanel(defaultPanelSide, result.Panel!);
                return ApplicationCommandResult.Rendered();
            case ModuleActionResultKind.Failed:
                new MessageDialog(_screen, _palette()).Show("Module", result.Message ?? "Module operation failed.");
                return ApplicationCommandResult.Rendered();
            default:
                return ApplicationCommandResult.Rendered();
        }
    }
}
