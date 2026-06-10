using CSharpFar.Core.Models;
using CSharpFar.App.Bootstrap;
using CSharpFar.Module.Abstractions;

namespace CSharpFar.App.Modules;

internal sealed class ApplicationModulePanelHost : IModulePanelHost
{
    private readonly ApplicationServiceCallbacks _callbacks;

    public ApplicationModulePanelHost(ApplicationServiceCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    public PanelSide ActiveSide => _callbacks.GetActiveSide();

    public FilePanelState GetPanelState(PanelSide panelSide) =>
        _callbacks.GetPanelState(panelSide);

    public void OpenPanel(PanelSide panelSide, IModulePanel panel) =>
        _callbacks.OpenModulePanel(panelSide, panel);

    public void RefreshPanels() =>
        _callbacks.RefreshPanels();
}
