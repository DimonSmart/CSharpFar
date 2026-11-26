using CSharpFar.App.Panels;
using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;

namespace CSharpFar.App.Modules;

internal sealed class ApplicationModulePanelHost(
    PanelWorkspaceController workspace,
    Action<PanelSide, IModulePanel> openPanel,
    Action refreshPanels) : IModulePanelHost
{
    public PanelSide ActiveSide => workspace.ActiveSide;
    public FilePanelState GetPanelState(PanelSide panelSide) => workspace.GetPanelState(panelSide);
    public void OpenPanel(PanelSide panelSide, IModulePanel panel) => openPanel(panelSide, panel);
    public void RefreshPanels() => refreshPanels();
}
