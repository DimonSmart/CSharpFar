using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct RoutedScrollableViewportInputResult(
    ScrollableViewportInputResult ViewportResult,
    UiInputResult UiResult);

/// <summary>Adapts a non-selectable scrollable viewport to routed UI input and interaction metadata.</summary>
public sealed class RoutedScrollableViewport
{
    public RoutedScrollableViewport(
        ScrollableViewport viewport,
        UiTargetId contentTarget,
        UiTargetId scrollbarTarget)
    {
        Viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        ContentTarget = contentTarget;
        ScrollbarTarget = scrollbarTarget;
    }

    public ScrollableViewport Viewport { get; }

    public UiTargetId ContentTarget { get; }

    public UiTargetId ScrollbarTarget { get; }

    public ScrollableViewportFrameState CalculateFrame(
        int totalItems,
        int viewportItems,
        Rect contentBounds,
        Rect? scrollbarBounds) =>
        Viewport.CalculateFrameState(totalItems, viewportItems, contentBounds, scrollbarBounds);

    public void ApplyCommittedFrame(ScrollableViewportFrameState frame) =>
        Viewport.ApplyCommittedFrame(frame);

    public UiInteractionFragment BuildInteractionFragment(ScrollableViewportFrameState frame)
    {
        var builder = new UiInteractionFrameBuilder();
        if (frame.ContentBounds.Width > 0 && frame.ContentBounds.Height > 0)
            builder.AddHitRegion(ContentTarget, frame.ContentBounds);
        if (frame.ScrollbarBounds is Rect scrollbarBounds)
            builder.AddHitRegion(ScrollbarTarget, scrollbarBounds);
        return builder.BuildFragment();
    }

    public bool IsTargetRoute(UiInputRouteContext route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route.RouteKind == UiInputRouteKind.HitTarget &&
               (route.Target == ContentTarget || route.Target == ScrollbarTarget) ||
               route.RouteKind == UiInputRouteKind.CapturedTarget && route.Target == ScrollbarTarget;
    }

    public RoutedScrollableViewportInputResult RouteInput(
        ConsoleInputEvent input,
        ScrollableViewportFrameState frame,
        UiInputRouteContext route,
        int wheelStep = 3)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(route);

        ApplyCommittedFrame(frame);
        ScrollableViewportInputResult result = input switch
        {
            KeyConsoleInputEvent key => Viewport.HandleKey(key.Key, frame),
            MouseConsoleInputEvent mouse when IsTargetRoute(route) => Viewport.HandleMouse(mouse, frame, wheelStep),
            _ => ScrollableViewportInputResult.NotHandled,
        };
        return new RoutedScrollableViewportInputResult(
            result,
            ScrollableViewportRouting.ToUiInputResult(result, ScrollbarTarget));
    }
}
