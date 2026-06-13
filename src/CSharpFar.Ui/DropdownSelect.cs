using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class DropdownSelect<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly Func<T, string> _itemText;
    private ScreenSnapshot? _underlay;
    private ScrollBarDragState? _scrollbarDrag;
    private int _selectedIndexBeforeOpen;

    public DropdownSelect(IReadOnlyList<T> items, Func<T, string> itemText)
    {
        if (items.Count == 0)
            throw new ArgumentException("Dropdown requires at least one item.", nameof(items));

        _items = items;
        _itemText = itemText;
    }

    public int SelectedIndex { get; set; }

    public int ScrollTop { get; set; }

    public bool IsOpen { get; private set; }

    public int MaxVisibleRows { get; set; } = 6;

    public T SelectedItem => _items[Math.Clamp(SelectedIndex, 0, _items.Count - 1)];

    public void Open(ConsoleSize size, Rect fieldBounds)
    {
        if (!IsOpen)
            _selectedIndexBeforeOpen = SelectedIndex;

        IsOpen = true;
        EnsureSelectedVisible(ContentRows(size, fieldBounds));
    }

    public void Close(ScreenRenderer screen, bool commit = false)
    {
        if (IsOpen && !commit)
            SelectedIndex = Math.Clamp(_selectedIndexBeforeOpen, 0, _items.Count - 1);

        if (_underlay is not null)
        {
            screen.Restore(_underlay);
            _underlay = null;
        }

        IsOpen = false;
        _scrollbarDrag = null;
    }

    public void Toggle(ScreenRenderer screen, ConsoleSize size, Rect fieldBounds)
    {
        if (IsOpen)
            Close(screen, commit: false);
        else
            Open(size, fieldBounds);
    }

    public void RenderField(
        ScreenRenderer screen,
        Rect fieldBounds,
        CellStyle style)
    {
        string label = _itemText(SelectedItem);
        string text = fieldBounds.Width > 1
            ? Fit(label, fieldBounds.Width - 1) + "\u2193"
            : "\u2193";
        screen.Write(fieldBounds.X, fieldBounds.Y, text, style);
    }

    public void RenderPopup(
        ScreenRenderer screen,
        ConsoleSize size,
        Rect fieldBounds)
    {
        if (!IsOpen)
        {
            if (_underlay is not null)
            {
                screen.Restore(_underlay);
                _underlay = null;
            }
            return;
        }

        Rect bounds = PopupBounds(size, fieldBounds);
        _underlay ??= screen.Capture(bounds);
        int contentRows = ContentRows(size, fieldBounds);
        EnsureSelectedVisible(contentRows);
        var palette = UiTheme.Current;

        var scrollState = contentRows > 0 && _items.Count > contentRows
            ? new ScrollState
            {
                TotalItems = _items.Count,
                ViewportItems = contentRows,
                FirstVisibleIndex = ScrollTop,
            }
            : null;
        var options = PaletteStyles.DialogPopupOptions(palette) with
        {
            DrawDoubleBorder = false,
            VerticalScrollState = scrollState,
        };
        var normalStyle = PaletteStyles.DialogFill(palette);
        var selectedStyle = PaletteStyles.InputHighlight(palette);

        new PopupRenderer().RenderPopup(screen, bounds, options, (_, contentBounds) =>
        {
            int textWidth = Math.Max(1, contentBounds.Width - 1);
            for (int row = 0; row < contentRows; row++)
            {
                int itemIndex = ScrollTop + row;
                string text = itemIndex < _items.Count ? _itemText(_items[itemIndex]) : string.Empty;
                screen.Write(
                    contentBounds.X,
                    contentBounds.Y + row,
                    Fit(text, textWidth),
                    itemIndex == SelectedIndex ? selectedStyle : normalStyle);
            }
        });
    }

    public bool TryHandleFieldMouse(
        MouseConsoleInputEvent mouse,
        ScreenRenderer screen,
        ConsoleSize size,
        Rect fieldBounds)
    {
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind != MouseEventKind.Down ||
            mouse.Y != fieldBounds.Y ||
            mouse.X < fieldBounds.X ||
            mouse.X >= fieldBounds.Right)
        {
            return false;
        }

        Toggle(screen, size, fieldBounds);
        return true;
    }

    public bool TryHandlePopupMouse(
        MouseConsoleInputEvent mouse,
        ScreenRenderer screen,
        ConsoleSize size,
        Rect fieldBounds,
        out bool selected)
    {
        selected = false;
        if (!IsOpen)
            return false;

        Rect bounds = PopupBounds(size, fieldBounds);
        int contentRows = ContentRows(size, fieldBounds);

        if (contentRows > 0 && _items.Count > contentRows)
        {
            int selectedIndex = SelectedIndex;
            int scrollTop = ScrollTop;
            if (ScrollableListMouseHandler.TryHandleScrollbarMouse(
                    mouse,
                    new Rect(bounds.Right - 1, bounds.Y + 1, 1, contentRows),
                    _items.Count,
                    contentRows,
                    ref selectedIndex,
                    ref scrollTop,
                    ref _scrollbarDrag))
            {
                SelectedIndex = selectedIndex;
                ScrollTop = scrollTop;
                return true;
            }
        }

        if (mouse.Kind == MouseEventKind.Wheel)
        {
            if (mouse.Button == MouseButton.WheelUp)
                SelectedIndex = Math.Max(0, SelectedIndex - 1);
            else if (mouse.Button == MouseButton.WheelDown)
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + 1);
            else
                return false;

            EnsureSelectedVisible(contentRows);
            return true;
        }

        if (mouse.Kind != MouseEventKind.Down || mouse.Button != MouseButton.Left)
            return false;

        if (mouse.X < bounds.X || mouse.X >= bounds.Right || mouse.Y < bounds.Y || mouse.Y >= bounds.Bottom)
        {
            Close(screen);
            return true;
        }

        int itemRow = mouse.Y - bounds.Y - 1;
        if (itemRow < 0 || itemRow >= contentRows)
            return true;

        int itemIndex = ScrollTop + itemRow;
        if (itemIndex >= _items.Count)
            return true;

        SelectedIndex = itemIndex;
        selected = true;
        Close(screen, commit: true);
        return true;
    }

    public bool TryHandleKey(ConsoleKeyInfo key, ConsoleSize size, Rect fieldBounds, ScreenRenderer screen, out bool selected)
    {
        selected = false;
        if (!IsOpen)
        {
            if (key.Key is ConsoleKey.DownArrow or ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Spacebar)
            {
                Open(size, fieldBounds);
                return true;
            }

            return false;
        }

        int contentRows = ContentRows(size, fieldBounds);
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                Close(screen);
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                selected = true;
                Close(screen, commit: true);
                return true;
            case ConsoleKey.UpArrow:
                SelectedIndex = Math.Max(0, SelectedIndex - 1);
                EnsureSelectedVisible(contentRows);
                return true;
            case ConsoleKey.DownArrow:
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + 1);
                EnsureSelectedVisible(contentRows);
                return true;
            case ConsoleKey.PageUp:
                SelectedIndex = Math.Max(0, SelectedIndex - contentRows);
                EnsureSelectedVisible(contentRows);
                return true;
            case ConsoleKey.PageDown:
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + contentRows);
                EnsureSelectedVisible(contentRows);
                return true;
            case ConsoleKey.Home:
                SelectedIndex = 0;
                EnsureSelectedVisible(contentRows);
                return true;
            case ConsoleKey.End:
                SelectedIndex = _items.Count - 1;
                EnsureSelectedVisible(contentRows);
                return true;
            default:
                return false;
        }
    }

    public Rect PopupBounds(ConsoleSize size, Rect fieldBounds)
    {
        int contentRows = ContentRows(size, fieldBounds);
        int height = contentRows + 2;
        int y = fieldBounds.Y + 1;
        if (y + height > size.Height)
            y = Math.Max(0, fieldBounds.Y - height);

        return new Rect(fieldBounds.X, y, fieldBounds.Width, height);
    }

    public int ContentRows(ConsoleSize size, Rect fieldBounds)
    {
        int rowsBelow = Math.Max(0, size.Height - fieldBounds.Bottom - 2);
        int rowsAbove = Math.Max(0, fieldBounds.Y - 2);
        int available = Math.Max(rowsBelow, rowsAbove);
        int maxRows = Math.Min(MaxVisibleRows, _items.Count);
        return Math.Clamp(available, 0, maxRows);
    }

    private void EnsureSelectedVisible(int contentRows)
    {
        SelectedIndex = Math.Clamp(SelectedIndex, 0, _items.Count - 1);
        if (contentRows <= 0)
        {
            ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, _items.Count, 1);
            return;
        }

        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, _items.Count, contentRows);
        ScrollTop = ScrollStateCalculator.EnsureIndexVisible(SelectedIndex, ScrollTop, contentRows);
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, _items.Count, contentRows);
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
