using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct RoutedScrollableListInputResult(ScrollableListInputResult ListResult, UiInputResult UiResult);

public enum RoutedListFocusBehavior { None, Focusable, FocusOnPointer }
public enum RoutedListKeyboardRouting { FocusedTargetOnly, LayerAndFocusedTarget }
public enum ListConfirmationBehavior { EnterOnly, EnterOrDoubleClick, EnterOrMouseDown }

public sealed record RoutedScrollableListOptions(
    RoutedListFocusBehavior FocusBehavior,
    RoutedListKeyboardRouting KeyboardRouting,
    ListConfirmationBehavior Confirmation)
{
    public static RoutedScrollableListOptions SelectionDialog { get; } = new(RoutedListFocusBehavior.FocusOnPointer, RoutedListKeyboardRouting.FocusedTargetOnly, ListConfirmationBehavior.EnterOrDoubleClick);
    public static RoutedScrollableListOptions ListWithButtons { get; } = SelectionDialog;
    public static RoutedScrollableListOptions CommandCompletion { get; } = new(RoutedListFocusBehavior.None, RoutedListKeyboardRouting.LayerAndFocusedTarget, ListConfirmationBehavior.EnterOrDoubleClick);
    public static RoutedScrollableListOptions DropdownPopup { get; } = new(RoutedListFocusBehavior.None, RoutedListKeyboardRouting.FocusedTargetOnly, ListConfirmationBehavior.EnterOrMouseDown);
}

/// <summary>Adds routed targets and focus policy to a list state without mirroring its API.</summary>
public sealed class RoutedScrollableList<T>
{
    private readonly ScrollableListInputController _input;
    public RoutedScrollableList(ScrollableListState<T> state, UiTargetId listTarget, UiTargetId scrollbarTarget, RoutedScrollableListOptions? options = null)
    {
        _input = new(); State = state ?? throw new ArgumentNullException(nameof(state)); ListTarget = listTarget; ScrollbarTarget = scrollbarTarget;
        Options = options ?? RoutedScrollableListOptions.SelectionDialog;
    }
    public ScrollableListState<T> State { get; }
    public UiTargetId ListTarget { get; }
    public UiTargetId ScrollbarTarget { get; }
    public RoutedScrollableListOptions Options { get; }
    internal ScrollBarDragState? ScrollbarDragState => _input.DragState;
    /// <summary>Applies the accepted list frame so subsequent routed input uses committed scrollbar state.</summary>
    public void ApplyCommittedFrame(ScrollableListFrame frame) => _input.ApplyCommittedFrame(frame);

    public ScrollableListFrame CalculateFrame(Rect contentBounds, Rect? scrollbarBounds) => _input.CalculateFrame(State, contentBounds, scrollbarBounds);
    public void Render(IUiCanvas canvas, ScrollableListFrame frame, ScrollableListRenderOptions<T> presentation) => ScrollableListRenderer.Render(canvas, State, frame, presentation);

    public void RenderScrollbar(IUiCanvas canvas, ScrollableListFrame frame, CellStyle style)
    {
        if (frame.Scrollbar is not { Bounds: var bounds } || frame.ItemCount <= frame.ViewportRows) return;
        new ScrollBarRenderer().RenderVerticalScrollbar(canvas, bounds, new ScrollState { TotalItems = frame.ItemCount, ViewportItems = frame.ViewportRows, FirstVisibleIndex = frame.ScrollTop }, new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false }, style);
    }
    public UiInteractionFragment BuildInteractionFragment(ScrollableListFrame frame, int tabOrder, bool isEnabled = true)
    {
        var builder = new UiInteractionFrameBuilder();
        if (frame.ContentBounds.Width > 0 && frame.ContentBounds.Height > 0) builder.AddHitRegion(ListTarget, frame.ContentBounds);
        if (frame.ScrollbarBounds is { } scrollbar) builder.AddHitRegion(ScrollbarTarget, scrollbar);
        if (Options.FocusBehavior is RoutedListFocusBehavior.Focusable or RoutedListFocusBehavior.FocusOnPointer) builder.AddFocusEntry(ListTarget, tabOrder, isEnabled);
        return builder.BuildFragment();
    }
    public bool IsTargetRoute(UiInputRouteContext route) => route.Target == ListTarget || route.Target == ScrollbarTarget;
    public RoutedScrollableListInputResult RouteInput(ConsoleInputEvent input, ScrollableListFrame frame, UiInputRouteContext route)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(route);
        bool acceptsLayerKeyboard = Options.KeyboardRouting == RoutedListKeyboardRouting.LayerAndFocusedTarget && route.RouteKind == UiInputRouteKind.Layer && input is KeyConsoleInputEvent;
        if (!IsTargetRoute(route) && !acceptsLayerKeyboard) return new(ScrollableListInputResult.NotHandled, UiInputResult.NotHandled);
        ScrollableListInputResult result = input switch
        {
            KeyConsoleInputEvent { Key: var key } => _input.HandleKey(State, frame, key),
            MouseConsoleInputEvent mouse when route.Target == ListTarget => _input.HandleContentMouse(State, frame, mouse, Options.Confirmation == ListConfirmationBehavior.EnterOrMouseDown, Options.Confirmation is ListConfirmationBehavior.EnterOrDoubleClick or ListConfirmationBehavior.EnterOrMouseDown),
            MouseConsoleInputEvent mouse when route.Target == ScrollbarTarget => _input.HandleScrollbarMouse(State, frame, mouse),
            _ => ScrollableListInputResult.NotHandled,
        };
        UiInputResult ui = ScrollableListRouting.ToUiInputResult(result, ScrollbarTarget);
        if (result.IsHandled && Options.FocusBehavior == RoutedListFocusBehavior.FocusOnPointer && input is MouseConsoleInputEvent { Button: MouseButton.Left, Kind: MouseEventKind.Down })
            ui = new UiInputResult(ui.Handled, true, UiFocusRequest.Set(ListTarget), ui.MouseCaptureRequest);
        return new(result, ui);
    }
}
