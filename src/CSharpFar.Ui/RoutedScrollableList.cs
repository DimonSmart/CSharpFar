using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public readonly record struct RoutedScrollableListInputResult(ScrollableListInputResult ListResult, UiInputResult UiResult);
public readonly record struct RoutedScrollableListFrame(ScrollableListFrame List);

public readonly record struct RoutedScrollableListInteractionOptions
{
    public static RoutedScrollableListInteractionOptions Focusable { get; } = new() { PublishFocusEntry = true, FocusOnMouseDown = true };
    public bool PublishFocusEntry { get; init; }
    public bool FocusOnMouseDown { get; init; }
    public bool AcceptKeyboardFromLayerRoute { get; init; }
}

/// <summary>Adds routed targets and focus policy to a list state without mirroring its API.</summary>
public sealed class RoutedScrollableList<T>
{
    private readonly ScrollableListInputController _input;
    public RoutedScrollableList(ScrollableListState<T> state, UiTargetId listTarget, UiTargetId scrollbarTarget, RoutedScrollableListInteractionOptions? interactionOptions = null)
    {
        _input = new(); State = state ?? throw new ArgumentNullException(nameof(state)); ListTarget = listTarget; ScrollbarTarget = scrollbarTarget;
        InteractionOptions = interactionOptions ?? RoutedScrollableListInteractionOptions.Focusable;
    }
    public ScrollableListState<T> State { get; }
    public UiTargetId ListTarget { get; }
    public UiTargetId ScrollbarTarget { get; }
    public RoutedScrollableListInteractionOptions InteractionOptions { get; }
    internal ScrollBarDragState? ScrollbarDragState => _input.DragState;
    internal void SynchronizeCommittedScrollbar(RoutedScrollableListFrame frame) => _input.Synchronize(frame.List);

    public RoutedScrollableListFrame CalculateFrame(Rect contentBounds, Rect? scrollbarBounds) => new(_input.CalculateFrame(State, contentBounds, scrollbarBounds));
    public void Render(IUiCanvas canvas, RoutedScrollableListFrame frame, ScrollableListRenderOptions<T> presentation) => ScrollableListRenderer.Render(canvas, State, frame.List, presentation);

    public void RenderScrollbar(IUiCanvas canvas, RoutedScrollableListFrame frame, CellStyle style)
    {
        if (frame.List.Scrollbar is not { Bounds: var bounds } || frame.List.ItemCount <= frame.List.ViewportRows) return;
        new ScrollBarRenderer().RenderVerticalScrollbar(canvas, bounds, new ScrollState { TotalItems = frame.List.ItemCount, ViewportItems = frame.List.ViewportRows, FirstVisibleIndex = frame.List.ScrollTop }, new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false }, style);
    }
    public UiInteractionFragment BuildInteractionFragment(RoutedScrollableListFrame frame, int tabOrder, bool isEnabled = true)
    {
        var builder = new UiInteractionFrameBuilder();
        if (frame.List.ContentBounds.Width > 0 && frame.List.ContentBounds.Height > 0) builder.AddHitRegion(ListTarget, frame.List.ContentBounds);
        if (frame.List.ScrollbarBounds is { } scrollbar) builder.AddHitRegion(ScrollbarTarget, scrollbar);
        if (InteractionOptions.PublishFocusEntry) builder.AddFocusEntry(ListTarget, tabOrder, isEnabled);
        return builder.BuildFragment();
    }
    public bool IsTargetRoute(UiInputRouteContext route) => route.Target == ListTarget || route.Target == ScrollbarTarget;
    public RoutedScrollableListInputResult RouteInput(ConsoleInputEvent input, RoutedScrollableListFrame frame, UiInputRouteContext route, bool confirmOnMouseDown = false, bool confirmOnDoubleClick = true)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(route);
        bool acceptsLayerKeyboard = InteractionOptions.AcceptKeyboardFromLayerRoute && route.RouteKind == UiInputRouteKind.Layer && input is KeyConsoleInputEvent;
        if (!IsTargetRoute(route) && !acceptsLayerKeyboard) return new(ScrollableListInputResult.NotHandled, UiInputResult.NotHandled);
        ScrollableListInputResult result = input switch
        {
            KeyConsoleInputEvent { Key: var key } => _input.HandleKey(State, frame.List, key),
            MouseConsoleInputEvent mouse when route.Target == ListTarget => _input.HandleContentMouse(State, frame.List, mouse, confirmOnMouseDown, confirmOnDoubleClick),
            MouseConsoleInputEvent mouse when route.Target == ScrollbarTarget => _input.HandleScrollbarMouse(State, frame.List, mouse),
            _ => ScrollableListInputResult.NotHandled,
        };
        UiInputResult ui = ScrollableListRouting.ToUiInputResult(result, ScrollbarTarget);
        if (result.IsHandled && InteractionOptions.FocusOnMouseDown && input is MouseConsoleInputEvent { Button: MouseButton.Left, Kind: MouseEventKind.Down })
            ui = new UiInputResult(ui.Handled, true, UiFocusRequest.Set(ListTarget), ui.MouseCaptureRequest);
        return new(result, ui);
    }
}
