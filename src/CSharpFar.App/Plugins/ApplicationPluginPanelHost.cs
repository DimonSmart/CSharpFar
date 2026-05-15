using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Plugins;

internal sealed class ApplicationPluginPanelHost : IPluginPanelHost
{
    private readonly Application _application;

    public ApplicationPluginPanelHost(Application application)
    {
        _application = application;
    }

    public PanelSide ActiveSide => _application.ActiveSide;

    public FilePanelState GetPanelState(PanelSide panelSide) =>
        _application.GetPanelState(panelSide);

    public void OpenPanel(PanelSide panelSide, IPluginPanel panel) =>
        _application.OpenPluginPanel(panelSide, panel);

    public void RefreshPanels() =>
        _application.RefreshPanels();
}
