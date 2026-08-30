using CSharpFar.App;
using CSharpFar.App.Input;
using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal static class ApplicationInputDispatcherTestExtensions
{
    public static ApplicationRuntimeRenderRequest Handle(
        this ApplicationInputDispatcher dispatcher,
        UiRoutedInput<ApplicationUiFrame> routed)
    {
        ApplicationPointerInteraction? interaction = null;
        if (routed.Input is MouseConsoleInputEvent mouse && routed.Target == ApplicationTargetIds.CommandLine)
        {
            int position = routed.Frame.CommandLine.TextPositionFromX(mouse.X);
            RoutedPointerSelectionActionKind? action = (routed.RouteKind, mouse) switch
            {
                (UiInputRouteKind.HitTarget, { Button: MouseButton.Left, Kind: MouseEventKind.Down }) => RoutedPointerSelectionActionKind.SelectionStarted,
                (UiInputRouteKind.CapturedTarget, { Button: MouseButton.Left, Kind: MouseEventKind.Move }) => RoutedPointerSelectionActionKind.SelectionExtended,
                (UiInputRouteKind.CapturedTarget, { Button: MouseButton.Left, Kind: MouseEventKind.Up }) => RoutedPointerSelectionActionKind.SelectionCompleted,
                (UiInputRouteKind.HitTarget, { Button: MouseButton.Left, Kind: MouseEventKind.DoubleClick }) => RoutedPointerSelectionActionKind.WordSelectionRequested,
                (UiInputRouteKind.HitTarget, { Button: MouseButton.Right, Kind: MouseEventKind.Down }) => RoutedPointerSelectionActionKind.SecondaryActionRequested,
                _ => null,
            };
            interaction = action is { } kind ? new ApplicationCommandLineInteraction(new(kind, position)) : null;
        }
        if (interaction is null && routed.Input is MouseConsoleInputEvent { Button: MouseButton.Left, Kind: MouseEventKind.Down } && routed.Frame.FunctionKeyBar is { } keys)
        {
            ApplicationFunctionKeyHit? action = keys.Actions.FirstOrDefault(x => ApplicationTargetIds.FunctionKeyAction(x.Layer, x.Key) == routed.Target);
            if (action is not null) interaction = new ApplicationFunctionKeyInteraction(routed.Frame, action);
        }
        if (interaction is null && routed.Input is MouseConsoleInputEvent { Button: MouseButton.Left, Kind: MouseEventKind.Down } && routed.Frame.DirectoryShortcutBar is { } shortcuts)
        {
            ApplicationDirectoryShortcutHit? shortcut = shortcuts.Shortcuts.FirstOrDefault(x => ApplicationTargetIds.DirectoryShortcut(x.ShortcutNumber) == routed.Target);
            if (shortcut is not null) interaction = new ApplicationDirectoryShortcutInteraction(shortcut, routed.Frame.Keyboard.ActiveSide);
        }
        if (interaction is null && routed.Input is MouseConsoleInputEvent pointer && routed.RouteKind == UiInputRouteKind.HitTarget)
        {
            ApplicationPanelFrame? panel = new[] { routed.Frame.LeftPanel, routed.Frame.RightPanel }
                .FirstOrDefault(candidate => candidate is not null && (routed.Target == ApplicationTargetIds.Panel(candidate.Side) || candidate.VisibleItems.Any(hit => ApplicationTargetIds.PanelItem(candidate.Side, hit.ItemIndex) == routed.Target)));
            if (panel is not null)
            {
                ApplicationPanelItemHit? hit = panel.VisibleItems.FirstOrDefault(x => ApplicationTargetIds.PanelItem(panel.Side, x.ItemIndex) == routed.Target);
                RoutedPointerActionKind? kind = pointer switch
                {
                    { Button: MouseButton.Left, Kind: MouseEventKind.Down } when hit is not null => RoutedPointerActionKind.ItemPrimaryPressed,
                    { Button: MouseButton.Left, Kind: MouseEventKind.Down } => RoutedPointerActionKind.SurfacePressed,
                    { Button: MouseButton.WheelUp, Kind: MouseEventKind.Wheel } => RoutedPointerActionKind.WheelUp,
                    { Button: MouseButton.WheelDown, Kind: MouseEventKind.Wheel } => RoutedPointerActionKind.WheelDown,
                    _ => null,
                };
                if (kind is { } action) interaction = new ApplicationPanelInteraction(panel.Side, panel, new RoutedPointerAction<ApplicationPanelPointerTarget>(action, hit is null ? null : new(hit)));
            }
        }
        return dispatcher.Handle(new ApplicationUiInputPacket(routed, interaction));
    }

    public static ApplicationInputHandlingResult Handle(this ApplicationCommandLineInputHandler handler, MouseConsoleInputEvent input, ApplicationCommandLineFrame frame, UiInputRouteKind routeKind)
    {
        int position = frame.TextPositionFromX(input.X);
        RoutedPointerSelectionActionKind? kind = (routeKind, input) switch
        {
            (UiInputRouteKind.HitTarget, { Button: MouseButton.Left, Kind: MouseEventKind.Down }) => RoutedPointerSelectionActionKind.SelectionStarted,
            (UiInputRouteKind.CapturedTarget, { Button: MouseButton.Left, Kind: MouseEventKind.Move }) => RoutedPointerSelectionActionKind.SelectionExtended,
            (UiInputRouteKind.CapturedTarget, { Button: MouseButton.Left, Kind: MouseEventKind.Up }) => RoutedPointerSelectionActionKind.SelectionCompleted,
            (UiInputRouteKind.HitTarget, { Button: MouseButton.Left, Kind: MouseEventKind.DoubleClick }) => RoutedPointerSelectionActionKind.WordSelectionRequested,
            (UiInputRouteKind.HitTarget, { Button: MouseButton.Right, Kind: MouseEventKind.Down }) => RoutedPointerSelectionActionKind.SecondaryActionRequested,
            _ => null,
        };
        return kind is { } action ? handler.Handle(new ApplicationCommandLineInteraction(new RoutedPointerSelectionAction<int>(action, position))) : ApplicationInputHandlingResult.NotHandled;
    }

    public static ApplicationInputHandlingResult Handle(this ApplicationPanelInputHandler handler, MouseConsoleInputEvent input, ApplicationPanelFrame frame, UiInputRouteKind _, UiTargetId? target)
    {
        ApplicationPanelPointerTarget? item = target == ApplicationTargetIds.PanelRetry(frame.Side)
            ? new(IsRetry: true)
            : frame.VisibleItems.FirstOrDefault(hit => ApplicationTargetIds.PanelItem(frame.Side, hit.ItemIndex) == target) is { } hit ? new(hit) : null;
        RoutedPointerActionKind? kind = input switch
        {
            { Button: MouseButton.Left, Kind: MouseEventKind.Down } when item is not null => RoutedPointerActionKind.ItemPrimaryPressed,
            { Button: MouseButton.Left, Kind: MouseEventKind.Down } => RoutedPointerActionKind.SurfacePressed,
            { Button: MouseButton.Left, Kind: MouseEventKind.DoubleClick } when item is not null => RoutedPointerActionKind.ItemDoubleClicked,
            { Button: MouseButton.Right, Kind: MouseEventKind.Down } when item is not null => RoutedPointerActionKind.ItemSecondaryPressed,
            { Button: MouseButton.WheelUp, Kind: MouseEventKind.Wheel } => RoutedPointerActionKind.WheelUp,
            { Button: MouseButton.WheelDown, Kind: MouseEventKind.Wheel } => RoutedPointerActionKind.WheelDown,
            _ => null,
        };
        return kind is { } action ? handler.Handle(new ApplicationPanelInteraction(frame.Side, frame, new RoutedPointerAction<ApplicationPanelPointerTarget>(action, item))) : ApplicationInputHandlingResult.NotHandled;
    }

    public static ApplicationInputHandlingResult Handle(this ApplicationFunctionKeyBarInputHandler handler, MouseConsoleInputEvent _, ApplicationUiFrame frame, ApplicationFunctionKeyHit action, UiInputRouteKind __) =>
        handler.Handle(new ApplicationFunctionKeyInteraction(frame, action));
}
