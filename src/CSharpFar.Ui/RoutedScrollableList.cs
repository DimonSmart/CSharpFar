using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public readonly record struct RoutedScrollableListInputResult(
    ScrollableListInputResult ListResult,
    UiInputResult UiResult);

/// <summary>Configures how a routed list participates in its enclosing layer's interaction model.</summary>
public readonly record struct RoutedScrollableListInteractionOptions
{
    public static RoutedScrollableListInteractionOptions Focusable { get; } = new()
    {
        PublishFocusEntry = true,
        FocusOnMouseDown = true,
    };

    public bool PublishFocusEntry { get; init; }

    public bool FocusOnMouseDown { get; init; }

    public bool AcceptKeyboardFromLayerRoute { get; init; }
}

/// <summary>Adapts a selectable scrollable list to routed UI input and interaction metadata.</summary>
public sealed class RoutedScrollableList<T>
{
    public RoutedScrollableList(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        UiTargetId listTarget,
        UiTargetId scrollbarTarget,
        RoutedScrollableListInteractionOptions? interactionOptions = null)
        : this(new ScrollableList<T>(items, itemText), listTarget, scrollbarTarget, interactionOptions)
    {
    }

    public RoutedScrollableList(
        ScrollableList<T> list,
        UiTargetId listTarget,
        UiTargetId scrollbarTarget,
        RoutedScrollableListInteractionOptions? interactionOptions = null)
    {
        _list = list ?? throw new ArgumentNullException(nameof(list));
        ListTarget = listTarget;
        ScrollbarTarget = scrollbarTarget;
        InteractionOptions = interactionOptions ?? RoutedScrollableListInteractionOptions.Focusable;
    }

    private readonly ScrollableList<T> _list;

    public IReadOnlyList<T> Items => _list.Items;

    public Func<T, string> ItemText => _list.ItemText;

    public int Count => _list.Count;

    public bool HasItems => _list.HasItems;

    public T? SelectedItemOrDefault => _list.SelectedItemOrDefault;

    public int SelectedIndex
    {
        get => _list.SelectedIndex;
        set => _list.SelectedIndex = value;
    }

    public int ScrollTop
    {
        get => _list.ScrollTop;
        set => _list.ScrollTop = value;
    }

    public string? EmptyText
    {
        get => _list.EmptyText;
        set => _list.EmptyText = value;
    }

    public CellStyle NormalStyle
    {
        get => _list.NormalStyle;
        set => _list.NormalStyle = value;
    }

    public CellStyle SelectedStyle
    {
        get => _list.SelectedStyle;
        set => _list.SelectedStyle = value;
    }

    public CellStyle EmptyStyle
    {
        get => _list.EmptyStyle;
        set => _list.EmptyStyle = value;
    }

    public Action<T, int>? SelectionChanged
    {
        get => _list.SelectionChanged;
        set => _list.SelectionChanged = value;
    }

    public void ResetItems(IReadOnlyList<T> items, int selectedIndex = 0) =>
        _list.ResetItems(items, selectedIndex);

    public void ReplaceItems<TKey>(IReadOnlyList<T> items, Func<T, TKey> identityKey, int viewportRows)
        where TKey : notnull =>
        _list.ReplaceItems(items, identityKey, viewportRows);

    public void EnsureSelectedVisible(int viewportRows) => _list.EnsureSelectedVisible(viewportRows);

    public ScrollState? GetScrollState(int viewportRows) => _list.GetScrollState(viewportRows);

    public ScrollState? GetScrollState(int viewportRows, int scrollTop) =>
        _list.GetScrollState(viewportRows, scrollTop);

    public UiTargetId ListTarget { get; }

    public UiTargetId ScrollbarTarget { get; }

    public RoutedScrollableListInteractionOptions InteractionOptions { get; }

    public ScrollableListFrameState CalculateFrame(int viewportRows, Rect? scrollbarBounds) =>
        _list.CalculateFrameState(viewportRows, scrollbarBounds);

    public void Render(IUiCanvas canvas, Rect contentBounds, ScrollableListFrameState frame) =>
        _list.Render(canvas, contentBounds, frame);

    public void Render(
        IUiCanvas canvas,
        Rect contentBounds,
        ScrollableListFrameState frame,
        CellStyle normalStyle,
        CellStyle selectedStyle,
        CellStyle emptyStyle) =>
        _list.Render(canvas, contentBounds, frame, normalStyle, selectedStyle, emptyStyle);

    public void RenderScrollbar(IUiCanvas canvas, ScrollableListFrameState frame, CellStyle style)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (frame.ScrollbarBounds is not { } bounds ||
            GetScrollState(frame.ViewportRows, frame.ScrollTop) is not { } state)
        {
            return;
        }

        new ScrollBarRenderer().RenderVerticalScrollbar(
            canvas,
            bounds,
            state,
            new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false },
            style);
    }

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
        if (InteractionOptions.PublishFocusEntry)
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
        bool acceptsLayerKeyboard = InteractionOptions.AcceptKeyboardFromLayerRoute &&
            route.RouteKind == UiInputRouteKind.Layer &&
            input is KeyConsoleInputEvent;
        if (!IsTargetRoute(route) && !acceptsLayerKeyboard)
            return new RoutedScrollableListInputResult(ScrollableListInputResult.NotHandled, UiInputResult.NotHandled);

        ScrollableListInputResult result = input switch
        {
            KeyConsoleInputEvent { Key: var key } => _list.HandleKey(key, frame.ViewportRows),
            MouseConsoleInputEvent mouse => _list.HandleMouse(
                mouse,
                contentBounds,
                frame,
                confirmOnMouseDown,
                confirmOnDoubleClick),
            _ => ScrollableListInputResult.NotHandled,
        };
        UiInputResult uiResult = ScrollableListRouting.ToUiInputResult(result, ScrollbarTarget);
        if (result.IsHandled &&
            InteractionOptions.FocusOnMouseDown &&
            input is MouseConsoleInputEvent { Button: MouseButton.Left, Kind: MouseEventKind.Down })
        {
            uiResult = new UiInputResult(
                uiResult.Handled,
                true,
                UiFocusRequest.Set(ListTarget),
                uiResult.MouseCaptureRequest);
        }

        return new RoutedScrollableListInputResult(result, uiResult);
    }

    public void ApplyCommittedFrame(ScrollableListFrameState frame) => _list.ApplyCommittedFrame(frame);
}
