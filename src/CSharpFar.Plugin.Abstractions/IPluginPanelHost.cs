using CSharpFar.Core.Models;

namespace CSharpFar.Plugin.Abstractions;

public interface IPluginPanelHost
{
    PanelSide ActiveSide { get; }

    FilePanelState GetPanelState(PanelSide panelSide);

    void OpenPanel(PanelSide panelSide, IPluginPanel panel);

    void RefreshPanels();
}
