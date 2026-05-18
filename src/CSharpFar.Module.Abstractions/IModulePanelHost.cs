using CSharpFar.Core.Models;

namespace CSharpFar.Module.Abstractions;

public interface IModulePanelHost
{
    PanelSide ActiveSide { get; }

    FilePanelState GetPanelState(PanelSide panelSide);

    void OpenPanel(PanelSide panelSide, IModulePanel panel);

    void RefreshPanels();
}
