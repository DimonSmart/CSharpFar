using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct RoutedScrollableViewportInputResult(
    ScrollableViewportInputResult ViewportResult,
    UiInputResult UiResult);

/// <summary>Adapts a non-selectable scrollable viewport to routed UI input and interaction metadata.</summary>
public sealed class RoutedScrollableViewport
{
    private RoutedScrollableViewport(
        ScrollableViewport viewport,
        UiTargetId contentTarget,
        UiTargetId scrollbarTarget)
    {
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        ContentTarget = contentTarget;
        ScrollbarTarget = scrollbarTarget;
    }

    public RoutedScrollableViewport(UiTargetId contentTarget, UiTargetId scrollbarTarget)
        : this(new ScrollableViewport(), contentTarget, scrollbarTarget)
    {
    }

    private readonly ScrollableViewport _viewport;

    public int FirstVisibleIndex
    {
        get => _viewport.FirstVisibleIndex;
        set => _viewport.FirstVisibleIndex = value;
    }

    public UiTargetId ContentTarget { get; }

    public UiTargetId ScrollbarTarget { get; }

    public ScrollableViewportFrameState CalculateFrame(
        int totalItems,
        int viewportItems,
        Rect contentBounds,
        Rect? scrollbarBounds) =>
        _viewport.CalculateFrameState(totalItems, viewportItems, contentBounds, scrollbarBounds);

    public void ApplyCommittedFrame(ScrollableViewportFrameState frame) => _viewport.ApplyCommittedFrame(frame);

    public ScrollState? GetScrollState(ScrollableViewportFrameState frame) => _viewport.GetScrollState(frame);

    public void RenderScrollbar(IUiCanvas canvas, ScrollableViewportFrameState frame, CellStyle style)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (frame.ScrollbarBounds is not { } bounds || GetScrollState(frame) is not { } state)
            return;

        new ScrollBarRenderer().RenderVerticalScrollbar(
            canvas,
            bounds,
            state,
            new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false },
            style);
    }

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

        if (route.RouteKind != UiInputRouteKind.CapturedTarget || route.Target != ScrollbarTarget)
            ApplyCommittedFrame(frame);
        ScrollableViewportInputResult result = input switch
        {
            KeyConsoleInputEvent key => _viewport.HandleKey(key.Key, frame),
            MouseConsoleInputEvent mouse when route.Target == ContentTarget => _viewport.HandleContentMouse(mouse, frame, wheelStep),
            MouseConsoleInputEvent mouse when route.Target == ScrollbarTarget => _viewport.HandleScrollbarMouse(mouse, frame),
            _ => ScrollableViewportInputResult.NotHandled,
        };
        return new RoutedScrollableViewportInputResult(
            result,
            ScrollableViewportRouting.ToUiInputResult(result, ScrollbarTarget));
    }
}
