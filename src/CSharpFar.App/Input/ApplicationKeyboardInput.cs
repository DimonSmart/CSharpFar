using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed record ApplicationKeyboardInput(
    UiRoutedInput<ApplicationUiFrame> Routed,
    ConsoleKeyInfo Key,
    ApplicationKeyboardOwner Owner)
{
    public ApplicationUiFrame Frame => Routed.Frame;
    public PanelSide ActiveSide => Frame.Keyboard.ActiveSide;
    public UiTargetId? Target => Routed.Target;
    public UiInputRouteKind RouteKind => Routed.RouteKind;

    public ApplicationPanelFrame? ActivePanelFrame =>
        ActiveSide == PanelSide.Left ? Frame.LeftPanel : Frame.RightPanel;

    public ApplicationPanelFrame? PanelFrame(PanelSide side) =>
        side == PanelSide.Left ? Frame.LeftPanel : Frame.RightPanel;

    public ApplicationPanelKeyboardFrame ActivePanel =>
        Frame.Keyboard.ActivePanel;

    public ApplicationPanelKeyboardFrame Panel(PanelSide side) =>
        Frame.Keyboard.Panel(side);
}
