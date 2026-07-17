using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class ApplicationPanelCommandInvocationFactory
{
    public static ApplicationPanelCommandInvocation Create(ApplicationUiFrame frame)
    {
        PanelSide side = frame.Keyboard.ActiveSide;
        ApplicationPanelFrame? panel = side == PanelSide.Left ? frame.LeftPanel : frame.RightPanel;
        return new ApplicationPanelCommandInvocation(
            side,
            panel?.VisibleRows ?? 0,
            frame.Keyboard.Panel(side),
            frame.Keyboard.Panel(side == PanelSide.Left ? PanelSide.Right : PanelSide.Left));
    }
}
