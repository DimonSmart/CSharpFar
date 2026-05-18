using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;

namespace CSharpFar.App.Modules;

internal sealed class ApplicationModulePanelHost : IModulePanelHost
{
    private readonly Application _application;

    public ApplicationModulePanelHost(Application application)
    {
        _application = application;
    }

    public PanelSide ActiveSide => _application.ActiveSide;

    public FilePanelState GetPanelState(PanelSide panelSide) =>
        _application.GetPanelState(panelSide);

    public void OpenPanel(PanelSide panelSide, IModulePanel panel) =>
        _application.OpenModulePanel(panelSide, panel);

    public void RefreshPanels() =>
        _application.RefreshPanels();
}
