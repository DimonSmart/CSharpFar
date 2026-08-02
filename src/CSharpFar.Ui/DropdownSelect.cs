using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public enum DropdownInputResultKind
{
    NotHandled,
    Handled,
    Opened,
    PreviewChanged,
    Committed,
    CommittedWithoutChange,
    Canceled,
}

public readonly record struct DropdownInputResult(
    DropdownInputResultKind Kind,
    UiMouseCaptureRequestKind MouseCapture = UiMouseCaptureRequestKind.None)
{
    public static DropdownInputResult NotHandled => new(DropdownInputResultKind.NotHandled);
    public bool IsHandled => Kind != DropdownInputResultKind.NotHandled;
}

public sealed class DropdownSelect<T>
{
    private readonly ScrollableListState<T> _state;
    private readonly ScrollableListInputController _input = new();
    private readonly Func<T, string> _itemText;
    private int _selectedIndexBeforeOpen;
    public DropdownSelect(IReadOnlyList<T> items, Func<T, string> itemText)
    {
        if (items is null || items.Count == 0) throw new ArgumentException("Dropdown requires at least one item.", nameof(items));
        _state = new ScrollableListState<T>(items); _itemText = itemText ?? throw new ArgumentNullException(nameof(itemText));
    }
    public int SelectedIndex { get => _state.SelectedIndex; set => _state.SetSelectedIndex(value, 1); }
    public int ScrollTop => _state.ScrollTop;
    public bool IsOpen { get; private set; }
    public int SelectionBeforeOpen => _selectedIndexBeforeOpen;
    private int _maxVisibleRows = 6;
    public int MaxVisibleRows
    {
        get => _maxVisibleRows;
        set => _maxVisibleRows = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }
    internal bool HasScrollbarDrag => _input.DragState is not null;
    public T SelectedItem => _state.Items[_state.SelectedIndex];
    public void Open() { if (!IsOpen) _selectedIndexBeforeOpen = SelectedIndex; IsOpen = true; }
    public void Close(bool commit = false) { if (IsOpen && !commit) SelectedIndex = _selectedIndexBeforeOpen; IsOpen = false; }
    public void Toggle() { if (IsOpen) Close(); else Open(); }
    public void RenderField(IUiCanvas screen, Rect fieldBounds, CellStyle style)
    {
        string text = fieldBounds.Width > 1 ? ConsoleTextMetrics.FitToCells(_itemText(SelectedItem), fieldBounds.Width - 1) + "↓" : "↓";
        screen.Write(fieldBounds.X, fieldBounds.Y, text, style);
    }
    public DropdownSelectFrame CalculateFrame(ConsoleSize size, Rect fieldBounds)
    {
        if (!IsOpen)
            return DropdownSelectFrame.Create(size, fieldBounds, new DropdownSelectStateSnapshot(_state.SelectedIndex, _state.ScrollTop, _selectedIndexBeforeOpen), null, _state.Count);
        Rect bounds = PopupBounds(size, fieldBounds); Rect content = PopupRenderer.GetContentBounds(bounds, drawBorder: true);
        Rect? scrollbar = content.Height > 0 && _state.Count > Math.Max(1, content.Height) ? new Rect(bounds.Right - 1, content.Y, 1, content.Height) : null;
        ScrollableListFrame list = _input.CalculateFrame(_state, content, scrollbar);
        return DropdownSelectFrame.Create(size, fieldBounds, new DropdownSelectStateSnapshot(list.SelectedIndex, list.ScrollTop, _selectedIndexBeforeOpen), new DropdownSelectPopupFrame(bounds, list), _state.Count);
    }
    public void RenderPopup(IUiCanvas screen, DropdownSelectFrame frame)
    {
        if (frame.Popup is not { } popup) return;
        var palette = UiTheme.Current;
        var options = PaletteStyles.DialogPopupOptions(palette) with { DrawDoubleBorder = false, VerticalScrollState = popup.List.ItemCount > popup.List.ViewportRows ? new ScrollState { TotalItems = popup.List.ItemCount, ViewportItems = popup.List.ViewportRows, FirstVisibleIndex = popup.List.ScrollTop } : null };
        new PopupRenderer().RenderPopup(screen, popup.Bounds, options, (_, _) => ScrollableListRenderer.Render(screen, _state, popup.List, new(_itemText, string.Empty, PaletteStyles.DialogFill(palette), PaletteStyles.InputHighlight(palette), PaletteStyles.DialogFill(palette))));
    }
    public DropdownInputResult TryHandleFieldMouse(MouseConsoleInputEvent mouse, DropdownSelectFrame frame)
    {
        ValidateFrame(frame);
        if (mouse.Button != MouseButton.Left || mouse.Kind != MouseEventKind.Down || mouse.Y != frame.FieldBounds.Y || mouse.X < frame.FieldBounds.X || mouse.X >= frame.FieldBounds.Right)
            return DropdownInputResult.NotHandled;
        bool wasOpen = IsOpen;
        Toggle();
        return wasOpen ? new(DropdownInputResultKind.Canceled) : new(DropdownInputResultKind.Opened);
    }
    public DropdownInputResult TryHandlePopupContentMouse(MouseConsoleInputEvent mouse, DropdownSelectFrame frame)
    {
        ValidateFrame(frame);
        if (frame.Popup is not { } popup) return DropdownInputResult.NotHandled;
        if (!popup.List.ContentBounds.Contains(mouse.X, mouse.Y))
            return mouse.Kind == MouseEventKind.Down && mouse.Button == MouseButton.Left && popup.Bounds.Contains(mouse.X, mouse.Y)
                ? new(DropdownInputResultKind.Handled)
                : DropdownInputResult.NotHandled;
        ScrollableListInputResult result = _input.HandleContentMouse(_state, popup.List, mouse, true, true);
        if (!result.IsHandled) return DropdownInputResult.NotHandled;
        if (result.Kind == ScrollableListInputResultKind.Confirmed)
        {
            bool changed = SelectedIndex != _selectedIndexBeforeOpen;
            Close(commit: true);
            return new(changed ? DropdownInputResultKind.Committed : DropdownInputResultKind.CommittedWithoutChange);
        }
        return new(result.Kind == ScrollableListInputResultKind.SelectionChanged ? DropdownInputResultKind.PreviewChanged : DropdownInputResultKind.Handled);
    }
    public DropdownInputResult TryHandleScrollbarMouse(MouseConsoleInputEvent mouse, DropdownSelectFrame frame)
    {
        ValidateFrame(frame);
        if (frame.Popup is not { } popup) return DropdownInputResult.NotHandled;
        ScrollableListInputResult result = _input.HandleScrollbarMouse(_state, popup.List, mouse);
        return result.IsHandled ? new(DropdownInputResultKind.Handled, Capture(result)) : DropdownInputResult.NotHandled;
    }
    public DropdownInputResult TryHandleKey(ConsoleKeyInfo key, DropdownSelectFrame frame)
    {
        ValidateFrame(frame);
        if (!IsOpen)
        {
            if (key.Key is not (ConsoleKey.Enter or ConsoleKey.Spacebar or ConsoleKey.DownArrow or ConsoleKey.F4))
                return DropdownInputResult.NotHandled;
            Open();
            return new(DropdownInputResultKind.Opened);
        }
        if (key.Key == ConsoleKey.Escape)
        {
            Close();
            return new(DropdownInputResultKind.Canceled);
        }
        if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar)
        {
            bool changed = SelectedIndex != _selectedIndexBeforeOpen;
            Close(commit: true);
            return new(changed ? DropdownInputResultKind.Committed : DropdownInputResultKind.CommittedWithoutChange);
        }
        if (frame.Popup is not { } popup) return DropdownInputResult.NotHandled;
        ScrollableListInputResult result = _input.HandleKey(_state, popup.List, key);
        return result.IsHandled ? new(result.Kind == ScrollableListInputResultKind.SelectionChanged ? DropdownInputResultKind.PreviewChanged : DropdownInputResultKind.Handled) : DropdownInputResult.NotHandled;
    }
    public void ApplyCommittedFrame(DropdownSelectFrame frame)
    {
        ValidateFrame(frame);
        RestoreCommittedFrame(frame);
    }
    internal void RestoreCommittedFrame(DropdownSelectFrame frame)
    {
        _selectedIndexBeforeOpen = frame.State.SelectionBeforeOpen;
        IsOpen = frame.Popup is not null;
        _state.RestoreSnapshot(frame.State.SelectedIndex, frame.State.ScrollTop);
    }
    internal void SetScrollTopForTesting(int scrollTop) => _state.SetFromInput(_state.SelectedIndex, scrollTop, 1);
    private static UiMouseCaptureRequestKind Capture(ScrollableListInputResult result) => result.DragStarted
        ? UiMouseCaptureRequestKind.Capture
        : result.DragEnded ? UiMouseCaptureRequestKind.Release : UiMouseCaptureRequestKind.None;
    private void ValidateFrame(DropdownSelectFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.ItemCount != _state.Count)
            throw new InvalidOperationException("The committed dropdown frame belongs to a different item collection.");
        if (frame.State.SelectedIndex < 0 || frame.State.SelectedIndex >= _state.Count ||
            frame.State.SelectionBeforeOpen < 0 || frame.State.SelectionBeforeOpen >= _state.Count ||
            frame.State.ScrollTop < 0)
            throw new InvalidOperationException("The committed dropdown frame contains invalid selection or scroll state.");
    }
    public Rect PopupBounds(ConsoleSize size, Rect fieldBounds) { int rows = ContentRows(size, fieldBounds); int height = rows + 2; int y = fieldBounds.Y + 1; if (y + height > size.Height) y = Math.Max(0, fieldBounds.Y - height); return new Rect(fieldBounds.X, y, fieldBounds.Width, height); }
    public int ContentRows(ConsoleSize size, Rect fieldBounds) { int available = Math.Max(Math.Max(0, size.Height - fieldBounds.Bottom - 2), Math.Max(0, fieldBounds.Y - 2)); return Math.Clamp(available, 0, Math.Min(MaxVisibleRows, _state.Count)); }
}

public sealed record DropdownSelectPopupFrame(Rect Bounds, ScrollableListFrame List)
{
    public Rect ContentBounds => List.ContentBounds;
    public Rect? ScrollbarBounds => List.ScrollbarBounds;
    public int ContentRows => List.ViewportRows;
}
public sealed record DropdownSelectStateSnapshot(int SelectedIndex, int ScrollTop, int SelectionBeforeOpen);

public sealed class DropdownSelectFrame
{
    private DropdownSelectFrame(ConsoleSize size, Rect fieldBounds, DropdownSelectStateSnapshot state, DropdownSelectPopupFrame? popup, int itemCount)
    {
        Size = size;
        FieldBounds = fieldBounds;
        State = state ?? throw new ArgumentNullException(nameof(state));
        Popup = popup;
        ItemCount = itemCount;
    }
    public ConsoleSize Size { get; }
    public Rect FieldBounds { get; }
    public DropdownSelectStateSnapshot State { get; }
    public DropdownSelectPopupFrame? Popup { get; }
    public int ItemCount { get; }
    public bool IsOpen => Popup is not null;

    internal static DropdownSelectFrame Create(ConsoleSize size, Rect fieldBounds, DropdownSelectStateSnapshot state, DropdownSelectPopupFrame? popup, int itemCount)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (itemCount <= 0 || state.SelectedIndex < 0 || state.SelectedIndex >= itemCount ||
            state.SelectionBeforeOpen < 0 || state.SelectionBeforeOpen >= itemCount || state.ScrollTop < 0)
            throw new ArgumentOutOfRangeException(nameof(state), "Dropdown state is outside the item collection.");
        if (popup is not null && popup.List.ItemCount != itemCount)
            throw new ArgumentException("The popup list must represent the dropdown item collection.", nameof(popup));
        return new DropdownSelectFrame(size, fieldBounds, state, popup, itemCount);
    }
}
