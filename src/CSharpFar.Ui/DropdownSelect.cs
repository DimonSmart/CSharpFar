using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class DropdownSelect<T>
{
    private readonly ScrollableList<T> _list;
    private ScreenSnapshot? _underlay;
    private ScrollBarDragState? _scrollbarDrag;
    private int _selectedIndexBeforeOpen;

    public DropdownSelect(IReadOnlyList<T> items, Func<T, string> itemText)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("Dropdown requires at least one item.", nameof(items));

        _list = new ScrollableList<T>(items, itemText);
    }

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

    public bool IsOpen { get; private set; }

    public int MaxVisibleRows { get; set; } = 6;

    public T SelectedItem => _list.Items[Math.Clamp(SelectedIndex, 0, _list.Count - 1)];

    public void Open(ConsoleSize size, Rect fieldBounds)
    {
        if (!IsOpen)
            _selectedIndexBeforeOpen = SelectedIndex;

        IsOpen = true;
        _list.Normalize(ContentRows(size, fieldBounds));
    }

    public void Close(ScreenRenderer screen, bool commit = false)
    {
        if (IsOpen && !commit)
            SelectedIndex = Math.Clamp(_selectedIndexBeforeOpen, 0, _list.Count - 1);

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
        string label = _list.ItemText(SelectedItem);
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
        _list.Normalize(contentRows);
        var palette = UiTheme.Current;

        var scrollState = contentRows > 0 ? _list.GetScrollState(contentRows) : null;
        var options = PaletteStyles.DialogPopupOptions(palette) with
        {
            DrawDoubleBorder = false,
            VerticalScrollState = scrollState,
        };
        var normalStyle = PaletteStyles.DialogFill(palette);
        var selectedStyle = PaletteStyles.InputHighlight(palette);
        _list.NormalStyle = normalStyle;
        _list.SelectedStyle = selectedStyle;
        _list.EmptyStyle = normalStyle;

        new PopupRenderer().RenderPopup(screen, bounds, options, (_, contentBounds) =>
        {
            int textWidth = Math.Max(1, contentBounds.Width - 1);
            _list.Render(screen, new Rect(contentBounds.X, contentBounds.Y, textWidth, contentRows));
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

        if (mouse.Kind == MouseEventKind.Down && mouse.Button == MouseButton.Left &&
            (mouse.X < bounds.X || mouse.X >= bounds.Right || mouse.Y < bounds.Y || mouse.Y >= bounds.Bottom))
        {
            Close(screen);
            return true;
        }

        var listInput = _list.HandleMouse(
            mouse,
            new Rect(bounds.X, bounds.Y + 1, bounds.Width, contentRows),
            contentRows > 0 ? new Rect(bounds.Right - 1, bounds.Y + 1, 1, contentRows) : null,
            contentRows,
            ref _scrollbarDrag,
            confirmOnMouseDown: true,
            confirmOnClick: true,
            confirmOnDoubleClick: true);
        if (!listInput.IsHandled)
            return mouse.Kind == MouseEventKind.Down && mouse.Button == MouseButton.Left &&
                mouse.X >= bounds.X && mouse.X < bounds.Right && mouse.Y >= bounds.Y && mouse.Y < bounds.Bottom;

        if (listInput.Kind == ScrollableListInputResultKind.Confirmed)
        {
            selected = true;
            Close(screen, commit: true);
        }
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
        }

        return _list.HandleKey(key, contentRows).IsHandled;
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
        int maxRows = Math.Min(MaxVisibleRows, _list.Count);
        return Math.Clamp(available, 0, maxRows);
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
