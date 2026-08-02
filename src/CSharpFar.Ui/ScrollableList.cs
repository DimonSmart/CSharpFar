using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public enum ScrollableListInputResultKind { NotHandled, Handled, SelectionChanged, Confirmed }

public readonly record struct ScrollableListInputResult(ScrollableListInputResultKind Kind, bool DragStarted = false, bool DragEnded = false)
{
    public static ScrollableListInputResult NotHandled => new(ScrollableListInputResultKind.NotHandled);
    public static ScrollableListInputResult Handled => new(ScrollableListInputResultKind.Handled);
    public static ScrollableListInputResult SelectionChanged => new(ScrollableListInputResultKind.SelectionChanged);
    public static ScrollableListInputResult Confirmed => new(ScrollableListInputResultKind.Confirmed);
    public bool IsHandled => Kind != ScrollableListInputResultKind.NotHandled;
}

[Obsolete("Use ScrollableListFrame.")]
public readonly record struct ScrollableListFrameState(int SelectedIndex, int ScrollTop, int ViewportRows = 1, VerticalScrollbarFrame? VerticalScrollbarFrame = null)
{
    public Rect? ScrollbarBounds => VerticalScrollbarFrame?.Bounds;
    public static ScrollableListFrameState Empty => new(-1, 0);
}

/// <summary>Logical list state. It deliberately has no rendering or input dependencies.</summary>
public sealed class ScrollableListState<T>
{
    private int _selectedIndex;
    private int _scrollTop;

    public ScrollableListState(IReadOnlyList<T> items, int selectedIndex = 0) => ResetItems(items, selectedIndex);

    public IReadOnlyList<T> Items { get; private set; } = [];
    public int Count => Items.Count;
    public bool HasItems => Count != 0;
    public int SelectedIndex => _selectedIndex;
    public int ScrollTop => _scrollTop;
    public T? SelectedItemOrDefault => HasItems ? Items[_selectedIndex] : default;

    public void ResetItems(IReadOnlyList<T> items, int selectedIndex = 0)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        _selectedIndex = HasItems ? Math.Clamp(selectedIndex, 0, Count - 1) : -1;
        _scrollTop = 0;
    }

    public void SelectIndex(int index, int viewportRows)
    {
        if (!HasItems) { _selectedIndex = -1; _scrollTop = 0; return; }
        _selectedIndex = Math.Clamp(index, 0, Count - 1);
        Normalize(viewportRows);
    }

    public void Restore(ScrollableListFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.ItemCount != Count)
            return;
        _selectedIndex = frame.SelectedIndex;
        _scrollTop = frame.ScrollTop;
    }

    public void ReplaceItems<TKey>(IReadOnlyList<T> items, Func<T, TKey> identityKey, int viewportRows) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(identityKey);
        int previousIndex = _selectedIndex;
        bool hadSelection = HasItems;
        TKey? key = hadSelection ? identityKey(Items[_selectedIndex]) : default;
        Items = items;
        if (!HasItems) { _selectedIndex = -1; _scrollTop = 0; return; }
        int index = -1;
        if (hadSelection)
            for (int i = 0; i < Count; i++)
                if (EqualityComparer<TKey>.Default.Equals(identityKey(Items[i]), key)) { index = i; break; }
        _selectedIndex = index >= 0 ? index : Math.Clamp(previousIndex, 0, Count - 1);
        Normalize(viewportRows);
    }

    internal void SetFromInput(int selectedIndex, int scrollTop, int viewportRows)
    {
        _selectedIndex = HasItems ? Math.Clamp(selectedIndex, 0, Count - 1) : -1;
        _scrollTop = HasItems ? Math.Max(0, scrollTop) : 0;
        Normalize(viewportRows);
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
    public VerticalScrollbarFrame? Scrollbar { get; }
    public Rect? ScrollbarBounds => Scrollbar?.Bounds;
    public static ScrollableListFrame Empty(Rect contentBounds, int viewportRows = 1) => new(contentBounds, 0, Math.Max(1, viewportRows), -1, 0, null);
    internal static ScrollableListFrame FromCommitted(Rect bounds, int count, int selectedIndex, int scrollTop, int viewportRows, VerticalScrollbarFrame? scrollbar = null)
    {
        int rows = Math.Max(1, viewportRows);
        if (count == 0) return Empty(bounds, rows);
        int selected = Math.Clamp(selectedIndex, 0, count - 1);
        int top = ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, count, rows);
        return new(bounds, count, rows, selected, top, scrollbar);
    }

    public static ScrollableListFrame Calculate<T>(ScrollableListState<T> state, Rect contentBounds, Rect? scrollbarBounds, VerticalScrollbarController scrollbarController)
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

public readonly record struct ScrollableListRenderOptions<T>(Func<T, string> ItemText, string EmptyText, CellStyle NormalStyle, CellStyle SelectedStyle, CellStyle EmptyStyle);

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

public sealed class ScrollableListInputController
{
    private readonly VerticalScrollbarController _scrollbar = new();
    internal ScrollBarDragState? DragState => _scrollbar.DragState;
    internal ScrollableListFrame CalculateFrame<T>(ScrollableListState<T> state, Rect bounds, Rect? scrollbarBounds) => ScrollableListFrame.Calculate(state, bounds, scrollbarBounds, _scrollbar);
    internal void Synchronize(ScrollableListFrame frame) => _scrollbar.ApplyCommittedFrame(frame.Scrollbar);

    public ScrollableListInputResult HandleKey<T>(ScrollableListState<T> state, ScrollableListFrame frame, ConsoleKeyInfo key)
    {
        Synchronize(frame);
        Restore(state, frame);
        if (key.Key == ConsoleKey.Enter) return state.HasItems ? ScrollableListInputResult.Confirmed : ScrollableListInputResult.Handled;
        int target = key.Key switch { ConsoleKey.UpArrow => frame.SelectedIndex - 1, ConsoleKey.DownArrow => frame.SelectedIndex + 1, ConsoleKey.PageUp => frame.SelectedIndex - frame.ViewportRows, ConsoleKey.PageDown => frame.SelectedIndex + frame.ViewportRows, ConsoleKey.Home => 0, ConsoleKey.End => frame.ItemCount - 1, _ => int.MinValue };
        return target == int.MinValue ? ScrollableListInputResult.NotHandled : Select(state, frame, target);
    }

    public ScrollableListInputResult HandleContentMouse<T>(ScrollableListState<T> state, ScrollableListFrame frame, MouseConsoleInputEvent mouse, bool confirmOnMouseDown = false, bool confirmOnDoubleClick = true)
    {
        Synchronize(frame);
        Restore(state, frame);
        if (mouse.Kind == MouseEventKind.Wheel)
            return mouse.Button == MouseButton.WheelUp ? Select(state, frame, frame.SelectedIndex - 1) : mouse.Button == MouseButton.WheelDown ? Select(state, frame, frame.SelectedIndex + 1) : ScrollableListInputResult.NotHandled;
        if (mouse.Button != MouseButton.Left || mouse.Kind is not (MouseEventKind.Down or MouseEventKind.DoubleClick) || !frame.ContentBounds.Contains(mouse.X, mouse.Y)) return ScrollableListInputResult.NotHandled;
        int index = frame.ScrollTop + mouse.Y - frame.ContentBounds.Y;
        if (index < 0 || index >= frame.ItemCount) return ScrollableListInputResult.NotHandled;
        bool changed = index != frame.SelectedIndex;
        if (changed) state.SetFromInput(index, frame.ScrollTop, frame.ViewportRows);
        bool confirm = mouse.Kind == MouseEventKind.Down ? confirmOnMouseDown : confirmOnDoubleClick;
        return confirm ? ScrollableListInputResult.Confirmed : changed ? ScrollableListInputResult.SelectionChanged : ScrollableListInputResult.Handled;
    }

    public ScrollableListInputResult HandleScrollbarMouse<T>(ScrollableListState<T> state, ScrollableListFrame frame, MouseConsoleInputEvent mouse)
    {
        Synchronize(frame);
        Restore(state, frame);
        if (frame.Scrollbar is not { } scrollbar) return ScrollableListInputResult.NotHandled;
        VerticalScrollbarInputResult result = _scrollbar.HandleMouse(mouse, scrollbar);
        if (!result.IsHandled) return ScrollableListInputResult.NotHandled;
        int selected = state.HasItems ? Math.Clamp(frame.SelectedIndex, result.FirstVisibleIndex, Math.Min(state.Count - 1, result.FirstVisibleIndex + frame.ViewportRows - 1)) : -1;
        bool changed = selected != frame.SelectedIndex;
        state.SetFromInput(selected, result.FirstVisibleIndex, frame.ViewportRows);
        return new(changed ? ScrollableListInputResultKind.SelectionChanged : ScrollableListInputResultKind.Handled, result.DragStarted, result.DragEnded);
    }

    private static void Restore<T>(ScrollableListState<T> state, ScrollableListFrame frame) { if (frame.ItemCount == state.Count) state.Restore(frame); }
    private static ScrollableListInputResult Select<T>(ScrollableListState<T> state, ScrollableListFrame frame, int target)
    {
        if (!state.HasItems) return ScrollableListInputResult.Handled;
        int index = Math.Clamp(target, 0, state.Count - 1);
        if (index == frame.SelectedIndex) return ScrollableListInputResult.Handled;
        state.SetFromInput(index, frame.ScrollTop, frame.ViewportRows);
        return ScrollableListInputResult.SelectionChanged;
    }
}

// Transitional source compatibility for consumers that have not yet been migrated.
// New code must use ScrollableListState, ScrollableListFrame, renderer and input controller directly.
public sealed class ScrollableList<T>
{
    private readonly ScrollableListInputController _input = new();
    internal ScrollableListInputController InputController => _input;
    public ScrollableList(IReadOnlyList<T> items, Func<T, string> itemText) { State = new(items); ItemText = itemText; }
    public ScrollableListState<T> State { get; }
    public IReadOnlyList<T> Items => State.Items; public Func<T, string> ItemText { get; }
    public int Count => State.Count; public bool HasItems => State.HasItems; public T? SelectedItemOrDefault => State.SelectedItemOrDefault;
    public int SelectedIndex { get => State.SelectedIndex; set => State.SelectIndex(value, 1); }
    public int ScrollTop { get => State.ScrollTop; set => State.SetFromInput(State.SelectedIndex, value, 1); }
    public string? EmptyText { get; set; }
    public CellStyle NormalStyle { get; set; } = CellStyle.Default; public CellStyle SelectedStyle { get; set; } = CellStyle.Default; public CellStyle EmptyStyle { get; set; } = CellStyle.Default;
    public Action<T, int>? SelectionChanged { get; set; }
    internal ScrollBarDragState? ScrollbarDragState => _input.DragState;
    public void ResetItems(IReadOnlyList<T> items, int selectedIndex = 0) => State.ResetItems(items, selectedIndex);
    public void ReplaceItems<TKey>(IReadOnlyList<T> items, Func<T, TKey> identityKey, int viewportRows) where TKey : notnull => State.ReplaceItems(items, identityKey, viewportRows);
    public void EnsureSelectedVisible(int viewportRows) => State.SelectIndex(State.SelectedIndex, viewportRows);
    public void Normalize(int viewportRows) => State.SelectIndex(State.SelectedIndex, viewportRows);
    public ScrollableListFrame CalculateFrame(int viewportRows, Rect? scrollbarBounds = null) => _input.CalculateFrame(State, new Rect(0, 0, 0, viewportRows), scrollbarBounds);
    [Obsolete("Use CalculateFrame.")]
    public ScrollableListFrameState CalculateFrameState(int viewportRows, Rect? scrollbarBounds = null)
    {
        ScrollableListFrame frame = CalculateFrame(viewportRows, scrollbarBounds);
        return new(frame.SelectedIndex, frame.ScrollTop, frame.ViewportRows, frame.Scrollbar);
    }
    public void ApplyCommittedFrame(ScrollableListFrame frame) { State.Restore(frame); _input.Synchronize(frame); }
    [Obsolete("Input accepts the committed frame directly.")]
    public void ApplyCommittedFrame(ScrollableListFrameState frame) => ApplyCommittedFrame(CalculateFrame(frame.ViewportRows, frame.ScrollbarBounds));
    public ScrollableListInputResult HandleKey(ConsoleKeyInfo key, int rows) => Notify(_input.HandleKey(State, CalculateFrame(rows), key));
    public ScrollableListInputResult HandleContentMouse(MouseConsoleInputEvent mouse, Rect bounds, ScrollableListFrame frame, bool confirmOnMouseDown = false, bool confirmOnDoubleClick = true) => Notify(_input.HandleContentMouse(State, frame, mouse, confirmOnMouseDown, confirmOnDoubleClick));
    [Obsolete("Use ScrollableListFrame.")]
    public ScrollableListInputResult HandleContentMouse(MouseConsoleInputEvent mouse, Rect bounds, ScrollableListFrameState frame, bool confirmOnMouseDown = false, bool confirmOnDoubleClick = true) => HandleContentMouse(mouse, bounds, ScrollableListFrame.FromCommitted(bounds, Count, frame.SelectedIndex, frame.ScrollTop, frame.ViewportRows, frame.VerticalScrollbarFrame), confirmOnMouseDown, confirmOnDoubleClick);
    public ScrollableListInputResult HandleScrollbarMouse(MouseConsoleInputEvent mouse, ScrollableListFrame frame) => Notify(_input.HandleScrollbarMouse(State, frame, mouse));
    [Obsolete("Use ScrollableListFrame.")]
    public ScrollableListInputResult HandleScrollbarMouse(MouseConsoleInputEvent mouse, ScrollableListFrameState frame) => HandleScrollbarMouse(mouse, CalculateFrame(frame.ViewportRows, frame.ScrollbarBounds));
    public void Render(IUiCanvas canvas, Rect bounds, ScrollableListFrame frame) => ScrollableListRenderer.Render(canvas, State, frame, new(ItemText, EmptyText ?? string.Empty, NormalStyle, SelectedStyle, EmptyStyle));
    public void Render(IUiCanvas canvas, Rect bounds) => Render(canvas, bounds, _input.CalculateFrame(State, bounds, null));
    public ScrollState? GetScrollState(int viewportRows) => Count > viewportRows ? new ScrollState { TotalItems = Count, ViewportItems = Math.Max(1, viewportRows), FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, Count, Math.Max(1, viewportRows)) } : null;
    private ScrollableListInputResult Notify(ScrollableListInputResult result) { if (result.Kind == ScrollableListInputResultKind.SelectionChanged && HasItems) SelectionChanged?.Invoke(Items[SelectedIndex], SelectedIndex); return result; }
}
