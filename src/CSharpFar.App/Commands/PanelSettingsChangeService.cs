using CSharpFar.App.Panels;
using CSharpFar.App.State;

namespace CSharpFar.App.Commands;

internal sealed class PanelSettingsChangeService(PanelRefreshService refresh, PanelSessionState panels, Action? saveSettings)
{
    public void Apply()
    {
        refresh.RefreshPanels(panels.Left, panels.Right);
        saveSettings?.Invoke();
    }
}
