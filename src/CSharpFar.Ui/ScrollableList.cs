using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public enum ScrollableListInputResultKind { NotHandled, Handled, SelectionChanged, Confirmed }
internal readonly record struct ScrollableListPosition(int ItemCount, int SelectedIndex, int ScrollTop);

public readonly record struct ScrollableListInputResult(ScrollableListInputResultKind Kind, bool DragStarted = false, bool DragEnded = false)
{
    public static ScrollableListInputResult NotHandled => new(ScrollableListInputResultKind.NotHandled);
    public static ScrollableListInputResult Handled => new(ScrollableListInputResultKind.Handled);
    public static ScrollableListInputResult SelectionChanged => new(ScrollableListInputResultKind.SelectionChanged);
    public static ScrollableListInputResult Confirmed => new(ScrollableListInputResultKind.Confirmed);
    public bool IsHandled => Kind != ScrollableListInputResultKind.NotHandled;
}

/// <summary>Logical list state. It deliberately has no rendering or input dependencies.</summary>
public sealed class ScrollableListState<T>
{
    private IReadOnlyList<T> _items = Array.Empty<T>();
    private int _selectedIndex;
    private int _scrollTop;

    public ScrollableListState(IReadOnlyList<T> items, int selectedIndex = 0) => ResetItems(items, selectedIndex);

    public IReadOnlyList<T> Items => _items;
    public int Count => Items.Count;
    public bool HasItems => Count != 0;
    public int SelectedIndex => _selectedIndex;
    public int ScrollTop => _scrollTop;
    public bool TryGetSelectedItem(out T item)
    {
        if (HasItems)
        {
            item = _items[_selectedIndex];
            return true;
        }

        item = default!;
        return false;
    }

    public void ResetItems(IReadOnlyList<T> items, int selectedIndex = 0)
    {
        _items = Snapshot(items);
        _selectedIndex = HasItems ? Math.Clamp(selectedIndex, 0, Count - 1) : -1;
        _scrollTop = 0;
    }

    public void SetSelectedIndex(int index, int viewportRows)
    {
        if (!TrySetSelectedIndex(index, viewportRows))
            throw new ArgumentOutOfRangeException(nameof(index), index, "The selected index must identify an item in the list.");
    }

    public bool TrySetSelectedIndex(int index, int viewportRows)
    {
        if (index < 0 || index >= Count)
            return false;

        _selectedIndex = index;
        Normalize(viewportRows);
        return true;
    }

    internal void ApplyPosition(ScrollableListPosition position, int viewportRows)
    {
        if (position.ItemCount != Count)
            throw new InvalidOperationException("The list frame does not belong to the current item collection.");
        _selectedIndex = HasItems ? Math.Clamp(position.SelectedIndex, 0, Count - 1) : -1;
        _scrollTop = HasItems ? Math.Max(0, position.ScrollTop) : 0;
        Normalize(viewportRows);
    }

    public void ReplaceItems<TKey>(IReadOnlyList<T> items, Func<T, TKey> identityKey, int viewportRows) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(identityKey);
        int previousIndex = _selectedIndex;
        bool hadSelection = HasItems;
        TKey? key = hadSelection ? identityKey(Items[_selectedIndex]) : default;
        _items = Snapshot(items);
        if (!HasItems) { _selectedIndex = -1; _scrollTop = 0; return; }
        int index = -1;
        if (hadSelection)
            for (int i = 0; i < Count; i++)
                if (EqualityComparer<TKey>.Default.Equals(identityKey(Items[i]), key)) { index = i; break; }
        _selectedIndex = index >= 0 ? index : Math.Clamp(previousIndex, 0, Count - 1);
        Normalize(viewportRows);
    }

    public void ReplaceItems(IReadOnlyList<T> items, int viewportRows)
    {
        ArgumentNullException.ThrowIfNull(items);
        int previousIndex = _selectedIndex;
        bool hadSelection = TryGetSelectedItem(out T selectedItem);
        _items = Snapshot(items);
        if (!HasItems) { _selectedIndex = -1; _scrollTop = 0; return; }

        int selectedIndex = -1;
        if (hadSelection)
            for (int index = 0; index < Count; index++)
                if (EqualityComparer<T>.Default.Equals(_items[index], selectedItem))
                {
                    selectedIndex = index;
                    break;
                }
        _selectedIndex = selectedIndex >= 0 ? selectedIndex : Math.Clamp(previousIndex, 0, Count - 1);
        Normalize(viewportRows);
    }

    internal bool MoveSelectionToClampedIndex(int index, int viewportRows)
    {
        if (!HasItems)
            return false;

        int clampedIndex = Math.Clamp(index, 0, Count - 1);
        if (clampedIndex == _selectedIndex)
            return false;

        _selectedIndex = clampedIndex;
        Normalize(viewportRows);
        return true;
    }

    private static IReadOnlyList<T> Snapshot(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Array.AsReadOnly(items.ToArray());
    }

    private void Normalize(int viewportRows)
    {
        if (!HasItems) { _selectedIndex = -1; _scrollTop = 0; return; }
        int rows = Math.Max(1, viewportRows);
        _scrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(_scrollTop, Count, rows);
        _scrollTop = ScrollStateCalculator.EnsureIndexVisible(_selectedIndex, _scrollTop, rows);
    }
}

/// <summary>An immutable, validated snapshot used by both list rendering and input.</summary>
public sealed class ScrollableListFrame
{
    private ScrollableListFrame(Rect contentBounds, int itemCount, int viewportRows, int selectedIndex, int scrollTop, VerticalScrollbarFrame? scrollbar)
    {
        ContentBounds = contentBounds; ItemCount = itemCount; ViewportRows = viewportRows;
        SelectedIndex = selectedIndex; ScrollTop = scrollTop; Scrollbar = scrollbar;
    }
    public Rect ContentBounds { get; }
    public int ItemCount { get; }
    public int ViewportRows { get; }
    public int SelectedIndex { get; }
    public int ScrollTop { get; }
    internal ScrollableListPosition Position => new(ItemCount, SelectedIndex, ScrollTop);
    public VerticalScrollbarFrame? Scrollbar { get; }
    public Rect? ScrollbarBounds => Scrollbar?.Bounds;
    internal static ScrollableListFrame Empty(Rect contentBounds, int viewportRows = 1) => new(contentBounds, 0, Math.Max(1, viewportRows), -1, 0, null);
    internal static ScrollableListFrame FromCommitted(Rect bounds, int count, int selectedIndex, int scrollTop, int viewportRows, VerticalScrollbarFrame? scrollbar = null)
    {
        int rows = Math.Max(1, viewportRows);
        if (count == 0) return Empty(bounds, rows);
        int selected = Math.Clamp(selectedIndex, 0, count - 1);
        int top = ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, count, rows);
        return new(bounds, count, rows, selected, top, scrollbar);
    }

    internal static ScrollableListFrame Calculate<T>(ScrollableListState<T> state, Rect contentBounds, Rect? scrollbarBounds, VerticalScrollbarController scrollbarController)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(scrollbarController);
        int rows = Math.Max(1, contentBounds.Height);
        if (!state.HasItems) return new(contentBounds, 0, rows, -1, 0, null);
        int selected = Math.Clamp(state.SelectedIndex, 0, state.Count - 1);
        int top = ScrollStateCalculator.ClampFirstVisibleIndex(state.ScrollTop, state.Count, rows);
        top = ScrollStateCalculator.EnsureIndexVisible(selected, top, rows);
        top = ScrollStateCalculator.ClampFirstVisibleIndex(top, state.Count, rows);
        ScrollState? scrollState = state.Count > rows ? new ScrollState { TotalItems = state.Count, ViewportItems = rows, FirstVisibleIndex = top } : null;
        VerticalScrollbarFrame? scrollbar = scrollState is null ? null : scrollbarController.CalculateFrame(scrollbarBounds, scrollState);
        return new(contentBounds, state.Count, rows, selected, top, scrollbar);
    }
}

public readonly record struct ScrollableListRenderOptions<T>
{
    public ScrollableListRenderOptions(Func<T, string> itemText, string emptyText, CellStyle normalStyle, CellStyle selectedStyle, CellStyle emptyStyle)
    {
        ItemText = itemText ?? throw new ArgumentNullException(nameof(itemText));
        EmptyText = emptyText;
        NormalStyle = normalStyle;
        SelectedStyle = selectedStyle;
        EmptyStyle = emptyStyle;
    }

    public Func<T, string> ItemText { get; init; }
    public string EmptyText { get; init; }
    public CellStyle NormalStyle { get; init; }
    public CellStyle SelectedStyle { get; init; }
    public CellStyle EmptyStyle { get; init; }
}

public static class ScrollableListRenderer
{
    public static void Render<T>(IUiCanvas canvas, ScrollableListState<T> state, ScrollableListFrame frame, ScrollableListRenderOptions<T> options)
    {
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(options.ItemText);
        canvas.FillRegion(frame.ContentBounds, options.NormalStyle);
        if (frame.ContentBounds.Width <= 0 || frame.ContentBounds.Height <= 0) return;
        if (!state.HasItems) { canvas.Write(frame.ContentBounds.X, frame.ContentBounds.Y, ConsoleTextMetrics.FitToCells(options.EmptyText, frame.ContentBounds.Width), options.EmptyStyle); return; }
        for (int row = 0; row < frame.ContentBounds.Height && frame.ScrollTop + row < state.Count; row++)
        {
            int index = frame.ScrollTop + row;
            canvas.Write(frame.ContentBounds.X, frame.ContentBounds.Y + row, ConsoleTextMetrics.FitToCells(options.ItemText(state.Items[index]), frame.ContentBounds.Width), index == frame.SelectedIndex ? options.SelectedStyle : options.NormalStyle);
        }
    }
}

internal sealed class ScrollableListInputController
{
    private readonly VerticalScrollbarController _scrollbar = new();
    internal ScrollBarDragState? DragState => _scrollbar.DragState;
    internal ScrollableListFrame CalculateFrame<T>(ScrollableListState<T> state, Rect bounds, Rect? scrollbarBounds) => ScrollableListFrame.Calculate(state, bounds, scrollbarBounds, _scrollbar);
    internal void Synchronize(ScrollableListFrame frame)
    {
        if (_scrollbar.DragState is null || frame.Scrollbar?.DragState is not null)
            _scrollbar.ApplyCommittedFrame(frame.Scrollbar);
    }
    internal void ApplyCommittedFrame(ScrollableListFrame frame) => _scrollbar.ApplyCommittedFrame(frame.Scrollbar);

    public ScrollableListInputResult HandleKey<T>(ScrollableListState<T> state, ScrollableListFrame frame, ConsoleKeyInfo key)
    {
        Synchronize(frame);
        ApplyCommittedPosition(state, frame);
        if (key.Key == ConsoleKey.Enter) return state.HasItems ? ScrollableListInputResult.Confirmed : ScrollableListInputResult.Handled;
        int target = key.Key switch { ConsoleKey.UpArrow => frame.SelectedIndex - 1, ConsoleKey.DownArrow => frame.SelectedIndex + 1, ConsoleKey.PageUp => frame.SelectedIndex - frame.ViewportRows, ConsoleKey.PageDown => frame.SelectedIndex + frame.ViewportRows, ConsoleKey.Home => 0, ConsoleKey.End => frame.ItemCount - 1, _ => int.MinValue };
        return target == int.MinValue ? ScrollableListInputResult.NotHandled : Select(state, frame, target);
    }

    public ScrollableListInputResult HandleContentMouse<T>(ScrollableListState<T> state, ScrollableListFrame frame, MouseConsoleInputEvent mouse, bool confirmOnMouseDown = false, bool confirmOnDoubleClick = true)
    {
        Synchronize(frame);
        ApplyCommittedPosition(state, frame);
        if (mouse.Kind == MouseEventKind.Wheel)
            return mouse.Button == MouseButton.WheelUp ? Select(state, frame, frame.SelectedIndex - 1) : mouse.Button == MouseButton.WheelDown ? Select(state, frame, frame.SelectedIndex + 1) : ScrollableListInputResult.NotHandled;
        if (mouse.Button != MouseButton.Left || mouse.Kind is not (MouseEventKind.Down or MouseEventKind.DoubleClick) || !frame.ContentBounds.Contains(mouse.X, mouse.Y)) return ScrollableListInputResult.NotHandled;
        int index = frame.ScrollTop + mouse.Y - frame.ContentBounds.Y;
        if (index < 0 || index >= frame.ItemCount) return ScrollableListInputResult.NotHandled;
        bool changed = index != frame.SelectedIndex;
        if (changed) state.ApplyPosition(new ScrollableListPosition(state.Count, index, frame.ScrollTop), frame.ViewportRows);
        bool confirm = mouse.Kind == MouseEventKind.Down ? confirmOnMouseDown : confirmOnDoubleClick;
        return confirm ? ScrollableListInputResult.Confirmed : changed ? ScrollableListInputResult.SelectionChanged : ScrollableListInputResult.Handled;
    }

    public ScrollableListInputResult HandleScrollbarMouse<T>(ScrollableListState<T> state, ScrollableListFrame frame, MouseConsoleInputEvent mouse)
    {
        Synchronize(frame);
        ApplyCommittedPosition(state, frame);
        if (frame.Scrollbar is not { } scrollbar) return ScrollableListInputResult.NotHandled;
        VerticalScrollbarInputResult result = _scrollbar.HandleMouse(mouse, scrollbar);
        if (!result.IsHandled) return ScrollableListInputResult.NotHandled;
        int selected = state.HasItems ? Math.Clamp(frame.SelectedIndex, result.FirstVisibleIndex, Math.Min(state.Count - 1, result.FirstVisibleIndex + frame.ViewportRows - 1)) : -1;
        bool changed = selected != frame.SelectedIndex;
        state.ApplyPosition(new ScrollableListPosition(state.Count, selected, result.FirstVisibleIndex), frame.ViewportRows);
        return new(changed ? ScrollableListInputResultKind.SelectionChanged : ScrollableListInputResultKind.Handled, result.DragStarted, result.DragEnded);
    }

    private static void ApplyCommittedPosition<T>(ScrollableListState<T> state, ScrollableListFrame frame) => state.ApplyPosition(frame.Position, frame.ViewportRows);
    private static ScrollableListInputResult Select<T>(ScrollableListState<T> state, ScrollableListFrame frame, int target)
    {
        if (!state.HasItems) return ScrollableListInputResult.Handled;
        return state.MoveSelectionToClampedIndex(target, frame.ViewportRows)
            ? ScrollableListInputResult.SelectionChanged
            : ScrollableListInputResult.Handled;
    }
}

