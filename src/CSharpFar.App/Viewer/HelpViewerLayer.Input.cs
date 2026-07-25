using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed partial class HelpViewerLayer
{
    protected override InteractiveSurfaceRouteResult<HelpAction> RouteSemanticInput(
        ConsoleInputEvent input,
        HelpViewerFrame frame,
        UiInputRouteContext context)
    {
        if (input is KeyConsoleInputEvent key &&
            context.RouteKind == UiInputRouteKind.KeyboardTarget &&
            context.Target == Keyboard)
        {
            var (action, invalidate) = RouteKey(key.Key, frame, context);
            return new InteractiveSurfaceRouteResult<HelpAction>(action, invalidate);
        }

        if (input is MouseConsoleInputEvent mouse)
            return RouteMouse(mouse, frame, context);

        return new InteractiveSurfaceRouteResult<HelpAction>(HelpAction.None);
    }

    private (HelpAction Action, bool Invalidate) RouteKey(
        ConsoleKeyInfo key,
        HelpViewerFrame frame,
        UiInputRouteContext context)
    {
        if (key.Key is ConsoleKey.F1 or ConsoleKey.F10 or ConsoleKey.Escape)
            return (HelpAction.Close, false);

        int oldLeft = _scrollLeft;
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                _scrollLeft = Math.Max(0, frame.ScrollLeft - 1);
                return (HelpAction.None, _scrollLeft != oldLeft);
            case ConsoleKey.RightArrow:
                _scrollLeft = Math.Min(frame.MaxScrollLeft, frame.ScrollLeft + 1);
                return (HelpAction.None, _scrollLeft != oldLeft);
            case ConsoleKey.Home:
            case ConsoleKey.End:
                _scrollLeft = 0;
                break;
        }

        RoutedScrollableViewportInputResult routed = _verticalViewport.RouteInput(
            new KeyConsoleInputEvent(key),
            frame.VerticalViewport,
            context);
        return routed.ViewportResult.IsHandled
            ? (HelpAction.None, routed.UiResult.Invalidate || _scrollLeft != oldLeft)
            : (HelpAction.None, false);
    }

    private InteractiveSurfaceRouteResult<HelpAction> RouteMouse(
        MouseConsoleInputEvent mouse,
        HelpViewerFrame frame,
        UiInputRouteContext context)
    {
        if (FunctionKeysController.TryGetAction(
                mouse,
                context,
                frame.FunctionKeyBarBounds.Y,
                frame.FunctionKeyBarBounds.Width,
                FunctionKeyActions,
                out HelpAction functionKeyAction))
        {
            return new InteractiveSurfaceRouteResult<HelpAction>(functionKeyAction);
        }

        if (!_verticalViewport.IsTargetRoute(context))
            return new InteractiveSurfaceRouteResult<HelpAction>(HelpAction.None);

        RoutedScrollableViewportInputResult routed = _verticalViewport.RouteInput(
            mouse,
            frame.VerticalViewport,
            context);
        return new InteractiveSurfaceRouteResult<HelpAction>(
            HelpAction.None,
            routed.UiResult.Invalidate,
            routed.UiResult.FocusRequest,
            routed.UiResult.MouseCaptureRequest);
    }
}
