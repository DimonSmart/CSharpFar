using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public readonly record struct RoutedScrollableListInputResult(
    ScrollableListInputResult ListResult,
    UiInputResult UiResult);

/// <summary>Adapts a selectable scrollable list to routed UI input and interaction metadata.</summary>
public sealed class RoutedScrollableList<T>
{
    public RoutedScrollableList(
        ScrollableList<T> list,
        UiTargetId listTarget,
        UiTargetId scrollbarTarget)
    {
        List = list ?? throw new ArgumentNullException(nameof(list));
        ListTarget = listTarget;
        ScrollbarTarget = scrollbarTarget;
    }

    public ScrollableList<T> List { get; }

    public UiTargetId ListTarget { get; }

    public UiTargetId ScrollbarTarget { get; }

    public ScrollableListFrameState CalculateFrame(int viewportRows, Rect? scrollbarBounds) =>
        List.CalculateFrameState(viewportRows, scrollbarBounds);

    public void Render(IUiCanvas canvas, Rect contentBounds, ScrollableListFrameState frame) =>
        List.Render(canvas, contentBounds, frame);

    public void Render(
        IUiCanvas canvas,
        Rect contentBounds,
        ScrollableListFrameState frame,
        CellStyle normalStyle,
        CellStyle selectedStyle,
        CellStyle emptyStyle) =>
        List.Render(canvas, contentBounds, frame, normalStyle, selectedStyle, emptyStyle);

    public UiInteractionFragment BuildInteractionFragment(
        Rect contentBounds,
        ScrollableListFrameState frame,
        int tabOrder,
        bool isEnabled = true)
    {
        var builder = new UiInteractionFrameBuilder();
        if (contentBounds.Width > 0 && contentBounds.Height > 0)
            builder.AddHitRegion(ListTarget, contentBounds);
        if (frame.ScrollbarBounds is Rect scrollbarBounds)
            builder.AddHitRegion(ScrollbarTarget, scrollbarBounds);
        builder.AddFocusEntry(ListTarget, tabOrder, isEnabled);
        return builder.BuildFragment();
    }

    public bool IsTargetRoute(UiInputRouteContext route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route.Target == ListTarget || route.Target == ScrollbarTarget;
    }

    public RoutedScrollableListInputResult RouteInput(
        ConsoleInputEvent input,
        Rect contentBounds,
        ScrollableListFrameState frame,
        UiInputRouteContext route,
        bool confirmOnMouseDown = false,
        bool confirmOnDoubleClick = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(route);

        ApplyCommittedFrame(frame);
        if (!IsTargetRoute(route))
            return new RoutedScrollableListInputResult(ScrollableListInputResult.NotHandled, UiInputResult.NotHandled);

        ScrollableListInputResult result = input switch
        {
            KeyConsoleInputEvent { Key: var key } => List.HandleKey(key, frame.ViewportRows),
            MouseConsoleInputEvent mouse => List.HandleMouse(
                mouse,
                contentBounds,
                frame,
                confirmOnMouseDown,
                confirmOnDoubleClick),
            _ => ScrollableListInputResult.NotHandled,
        };
        return new RoutedScrollableListInputResult(
            result,
            ScrollableListRouting.ToUiInputResult(result, ScrollbarTarget));
    }

    public void ApplyCommittedFrame(ScrollableListFrameState frame) => List.ApplyCommittedFrame(frame);
}
