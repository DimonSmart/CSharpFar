using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public enum ScrollableListInputResultKind
{
    NotHandled,
    Handled,
    SelectionChanged,
    Confirmed,
}

public readonly record struct ScrollableListInputResult(
    ScrollableListInputResultKind Kind,
    bool DragStarted = false,
    bool DragEnded = false)
{
    public static ScrollableListInputResult NotHandled => new(ScrollableListInputResultKind.NotHandled);
    public static ScrollableListInputResult Handled => new(ScrollableListInputResultKind.Handled);
    public static ScrollableListInputResult SelectionChanged => new(ScrollableListInputResultKind.SelectionChanged);
    public static ScrollableListInputResult Confirmed => new(ScrollableListInputResultKind.Confirmed);

    public bool IsHandled => Kind != ScrollableListInputResultKind.NotHandled;
}

public readonly record struct ScrollableListFrameState(
    int SelectedIndex,
    int ScrollTop,
    int ViewportRows = 1,
    Rect? ScrollbarBounds = null,
    ScrollBarDragState? ScrollbarDrag = null)
{
    public static ScrollableListFrameState Empty => new(-1, 0);
}

public sealed class ScrollableList<T>
{
    public ScrollableList(IReadOnlyList<T> items, Func<T, string> itemText)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        ItemText = itemText ?? throw new ArgumentNullException(nameof(itemText));
    }

    public IReadOnlyList<T> Items { get; }

    public Func<T, string> ItemText { get; }

    public int SelectedIndex { get; set; }

    public int ScrollTop { get; set; }

    public string? EmptyText { get; set; }

    public CellStyle NormalStyle { get; set; } = CellStyle.Default;

    public CellStyle SelectedStyle { get; set; } = CellStyle.Default;

    public CellStyle EmptyStyle { get; set; } = CellStyle.Default;

    public Action<T, int>? SelectionChanged { get; set; }

    private ScrollBarDragState? _scrollbarDrag;

    public int Count => Items.Count;

    public bool HasItems => Count > 0;

    public T? SelectedItemOrDefault =>
        SelectedIndex >= 0 && SelectedIndex < Count ? Items[SelectedIndex] : default;

    public ScrollBarDragState? ScrollbarDrag => _scrollbarDrag;

    public void Normalize(int viewportRows)
    {
        if (!HasItems)
        {
            SelectedIndex = -1;
            ScrollTop = 0;
            return;
        }

        SelectedIndex = Math.Clamp(SelectedIndex, 0, Count - 1);
        EnsureSelectedVisible(viewportRows);
    }

    /// <summary>Calculates normalized list state without changing input state.</summary>
    public ScrollableListFrameState CalculateFrameState(int viewportRows)
        => CalculateFrameState(viewportRows, scrollbarBounds: null);

    public ScrollableListFrameState CalculateFrameState(int viewportRows, Rect? scrollbarBounds)
    {
        int rows = Math.Max(1, viewportRows);
        if (!HasItems)
            return new ScrollableListFrameState(-1, 0, rows, scrollbarBounds, null);

        int selectedIndex = Math.Clamp(SelectedIndex, 0, Count - 1);
        int scrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, Count, rows);
        scrollTop = ScrollStateCalculator.EnsureIndexVisible(selectedIndex, scrollTop, rows);
        scrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, Count, rows);
        ScrollBarDragState? drag = _scrollbarDrag is { } currentDrag && scrollbarBounds is { } bounds
            ? ScrollBarInteraction.RebaseDrag(currentDrag, bounds, Count, rows)
            : null;
        return new ScrollableListFrameState(selectedIndex, scrollTop, rows, scrollbarBounds, drag);
    }

    public void ApplyCommittedFrame(ScrollableListFrameState frame)
    {
        SelectedIndex = frame.SelectedIndex;
        ScrollTop = frame.ScrollTop;
        _scrollbarDrag = frame.ScrollbarDrag;
    }

    public void EnsureSelectedVisible(int viewportRows)
    {
        if (!HasItems)
        {
            SelectedIndex = -1;
            ScrollTop = 0;
            return;
        }

        int rows = Math.Max(1, viewportRows);
        SelectedIndex = Math.Clamp(SelectedIndex, 0, Count - 1);
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, Count, rows);
        ScrollTop = ScrollStateCalculator.EnsureIndexVisible(SelectedIndex, ScrollTop, rows);
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, Count, rows);
    }

    public void Render(ScreenRenderer screen, Rect bounds)
        => Render(screen, bounds, new ScrollableListFrameState(SelectedIndex, ScrollTop));

    public void Render(ScreenRenderer screen, Rect bounds, ScrollableListFrameState frameState)
        => Render(screen, bounds, frameState, NormalStyle, SelectedStyle, EmptyStyle);

    public void Render(
        ScreenRenderer screen,
        Rect bounds,
        ScrollableListFrameState frameState,
        CellStyle normalStyle,
        CellStyle selectedStyle,
        CellStyle emptyStyle)
    {
        ArgumentNullException.ThrowIfNull(screen);

        screen.FillRegion(bounds, normalStyle);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (!HasItems)
        {
            screen.Write(bounds.X, bounds.Y, Fit(EmptyText ?? string.Empty, bounds.Width), emptyStyle);
            return;
        }

        for (int row = 0; row < bounds.Height; row++)
        {
            int index = frameState.ScrollTop + row;
            if (index >= Count)
                break;

            screen.Write(
                bounds.X,
                bounds.Y + row,
                Fit(ItemText(Items[index]), bounds.Width),
                index == frameState.SelectedIndex ? selectedStyle : normalStyle);
        }
    }

    public ScrollState? GetScrollState(int viewportRows)
        => GetScrollState(viewportRows, ScrollTop);

    public ScrollState? GetScrollState(int viewportRows, int scrollTop)
    {
        int rows = Math.Max(1, viewportRows);
        return Count > rows
            ? new ScrollState
            {
                TotalItems = Count,
                ViewportItems = rows,
                FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, Count, rows),
            }
            : null;
    }

    public ScrollableListInputResult HandleKey(ConsoleKeyInfo key, int viewportRows)
    {
        if (key.Key == ConsoleKey.Enter)
            return HasItems ? ScrollableListInputResult.Confirmed : ScrollableListInputResult.Handled;

        int target = key.Key switch
        {
            ConsoleKey.UpArrow => SelectedIndex - 1,
            ConsoleKey.DownArrow => SelectedIndex + 1,
            ConsoleKey.PageUp => SelectedIndex - Math.Max(1, viewportRows),
            ConsoleKey.PageDown => SelectedIndex + Math.Max(1, viewportRows),
            ConsoleKey.Home => 0,
            ConsoleKey.End => Count - 1,
            _ => int.MinValue,
        };

        if (target == int.MinValue)
            return ScrollableListInputResult.NotHandled;
        if (!HasItems)
            return ScrollableListInputResult.Handled;

        return ChangeSelection(target, viewportRows);
    }

    public ScrollableListInputResult HandleMouse(
        MouseConsoleInputEvent mouse,
        Rect contentBounds,
        Rect? scrollbarBounds,
        int viewportRows,
        ref ScrollBarDragState? scrollbarDrag,
        bool confirmOnMouseDown = false,
        bool confirmOnDoubleClick = true)
    {
        if (mouse.Kind == MouseEventKind.Wheel)
        {
            bool insideContent = contentBounds.Contains(mouse.X, mouse.Y);
            bool insideScrollbar = scrollbarBounds is Rect wheelScrollbar && wheelScrollbar.Contains(mouse.X, mouse.Y);
            if (!insideContent && !insideScrollbar)
                return ScrollableListInputResult.NotHandled;

            // Far-like wheel navigation moves selection and keeps the selected item visible.
            if (mouse.Button == MouseButton.WheelUp)
                return HasItems ? ChangeSelection(SelectedIndex - 1, viewportRows) : ScrollableListInputResult.Handled;
            if (mouse.Button == MouseButton.WheelDown)
                return HasItems ? ChangeSelection(SelectedIndex + 1, viewportRows) : ScrollableListInputResult.Handled;
            return ScrollableListInputResult.NotHandled;
        }

        if (scrollbarBounds is Rect scrollbar && Count > Math.Max(1, viewportRows))
        {
            int selectedIndex = SelectedIndex;
            int scrollTop = ScrollTop;
            bool wasDragging = scrollbarDrag.HasValue;
            if (ScrollableListMouseHandler.TryHandleScrollbarMouse(
                    mouse,
                    scrollbar,
                    Count,
                    Math.Max(1, viewportRows),
                    ref selectedIndex,
                    ref scrollTop,
                    ref scrollbarDrag))
            {
                bool changed = selectedIndex != SelectedIndex;
                SelectedIndex = selectedIndex;
                ScrollTop = scrollTop;
                if (changed)
                    NotifySelectionChanged();
                bool isDragging = scrollbarDrag.HasValue;
                return new ScrollableListInputResult(
                    changed ? ScrollableListInputResultKind.SelectionChanged : ScrollableListInputResultKind.Handled,
                    DragStarted: !wasDragging && isDragging,
                    DragEnded: wasDragging && !isDragging);
            }
        }

        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.DoubleClick) ||
            mouse.X < contentBounds.X ||
            mouse.X >= contentBounds.Right ||
            mouse.Y < contentBounds.Y ||
            mouse.Y >= contentBounds.Bottom)
        {
            return ScrollableListInputResult.NotHandled;
        }

        int index = ScrollTop + mouse.Y - contentBounds.Y;
        if (index < 0 || index >= Count)
            return ScrollableListInputResult.NotHandled;

        bool changedByClick = index != SelectedIndex;
        if (changedByClick)
        {
            SelectedIndex = index;
            EnsureSelectedVisible(viewportRows);
            NotifySelectionChanged();
        }

        bool confirmed = mouse.Kind switch
        {
            MouseEventKind.Down => confirmOnMouseDown,
            MouseEventKind.DoubleClick => confirmOnDoubleClick,
            _ => false,
        };
        if (confirmed)
            return ScrollableListInputResult.Confirmed;
        return changedByClick ? ScrollableListInputResult.SelectionChanged : ScrollableListInputResult.Handled;
    }

    public ScrollableListInputResult HandleMouse(
        MouseConsoleInputEvent mouse,
        Rect contentBounds,
        Rect? scrollbarBounds,
        int viewportRows,
        bool confirmOnMouseDown = false,
        bool confirmOnDoubleClick = true) =>
        HandleMouse(
            mouse,
            contentBounds,
            scrollbarBounds,
            viewportRows,
            ref _scrollbarDrag,
            confirmOnMouseDown,
            confirmOnDoubleClick);

    private ScrollableListInputResult ChangeSelection(int target, int viewportRows)
    {
        int selectedIndex = Math.Clamp(target, 0, Count - 1);
        if (selectedIndex == SelectedIndex)
        {
            EnsureSelectedVisible(viewportRows);
            return ScrollableListInputResult.Handled;
        }

        SelectedIndex = selectedIndex;
        EnsureSelectedVisible(viewportRows);
        NotifySelectionChanged();
        return ScrollableListInputResult.SelectionChanged;
    }

    private void NotifySelectionChanged()
    {
        if (HasItems)
            SelectionChanged?.Invoke(Items[SelectedIndex], SelectedIndex);
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
