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
    public RoutedScrollableList(IReadOnlyList<T> items, Func<T, string> itemText, UiTargetId listTarget, UiTargetId scrollbarTarget, RoutedScrollableListInteractionOptions? interactionOptions = null)
        : this(new ScrollableListState<T>(items), listTarget, scrollbarTarget, interactionOptions) => ItemText = itemText ?? throw new ArgumentNullException(nameof(itemText));
    public RoutedScrollableList(ScrollableList<T> list, UiTargetId listTarget, UiTargetId scrollbarTarget, RoutedScrollableListInteractionOptions? interactionOptions = null)
        : this(list?.State ?? throw new ArgumentNullException(nameof(list)), listTarget, scrollbarTarget, interactionOptions) { _input = list.InputController; ItemText = list.ItemText; }
    public ScrollableListState<T> State { get; }
    // Compatibility surface; new consumers use State and explicit presentation.
    public Func<T, string>? ItemText { get; }
    public IReadOnlyList<T> Items => State.Items; public int Count => State.Count; public bool HasItems => State.HasItems; public T? SelectedItemOrDefault => State.SelectedItemOrDefault;
    public int SelectedIndex { get => State.SelectedIndex; set => State.SelectIndex(value, 1); }
    public int ScrollTop { get => State.ScrollTop; set => State.SetFromInput(State.SelectedIndex, value, 1); }
    public string? EmptyText { get; set; }
    public CellStyle NormalStyle { get; set; } = CellStyle.Default; public CellStyle SelectedStyle { get; set; } = CellStyle.Default; public CellStyle EmptyStyle { get; set; } = CellStyle.Default;
    public Action<T, int>? SelectionChanged { get; set; }
    public UiTargetId ListTarget { get; }
    public UiTargetId ScrollbarTarget { get; }
    public RoutedScrollableListInteractionOptions InteractionOptions { get; }

    public RoutedScrollableListFrame CalculateFrame(Rect contentBounds, Rect? scrollbarBounds) => new(_input.CalculateFrame(State, contentBounds, scrollbarBounds));
    public RoutedScrollableListFrame CalculateFrame(int viewportRows, Rect contentBounds, Rect? scrollbarBounds) => CalculateFrame(contentBounds, scrollbarBounds);
    public void Render(IUiCanvas canvas, RoutedScrollableListFrame frame, ScrollableListRenderOptions<T> presentation) => ScrollableListRenderer.Render(canvas, State, frame.List, presentation);
    public void Render(IUiCanvas canvas, RoutedScrollableListFrame frame) => Render(canvas, frame, NormalStyle, SelectedStyle, EmptyStyle);
    public void Render(IUiCanvas canvas, RoutedScrollableListFrame frame, CellStyle normalStyle, CellStyle selectedStyle, CellStyle emptyStyle) =>
        Render(canvas, frame, new(ItemText ?? throw new InvalidOperationException("List presentation is required."), EmptyText ?? string.Empty, normalStyle, selectedStyle, emptyStyle));
    public void ResetItems(IReadOnlyList<T> items, int selectedIndex = 0) => State.ResetItems(items, selectedIndex);
    public void ReplaceItems<TKey>(IReadOnlyList<T> items, Func<T, TKey> identityKey, int viewportRows) where TKey : notnull => State.ReplaceItems(items, identityKey, viewportRows);
    public void EnsureSelectedVisible(int viewportRows) => State.SelectIndex(State.SelectedIndex, viewportRows);
    public ScrollState? GetScrollState(int viewportRows, int? scrollTop = null) => State.Count > viewportRows ? new ScrollState { TotalItems = State.Count, ViewportItems = Math.Max(1, viewportRows), FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop ?? State.ScrollTop, State.Count, Math.Max(1, viewportRows)) } : null;

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
    public void ApplyCommittedFrame(RoutedScrollableListFrame frame) { State.Restore(frame.List); _input.Synchronize(frame.List); }
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
        if (result.Kind == ScrollableListInputResultKind.SelectionChanged && State.HasItems)
            SelectionChanged?.Invoke(State.Items[State.SelectedIndex], State.SelectedIndex);
        return new(result, ui);
    }
}
