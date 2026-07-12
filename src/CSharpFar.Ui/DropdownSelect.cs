using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class DropdownSelect<T>
{
    private readonly ScrollableList<T> _list;
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
    }

    public void Close(bool commit = false)
    {
        if (IsOpen && !commit)
            SelectedIndex = Math.Clamp(_selectedIndexBeforeOpen, 0, _list.Count - 1);

        IsOpen = false;
        _scrollbarDrag = null;
    }

    public void Toggle(ConsoleSize size, Rect fieldBounds)
    {
        if (IsOpen)
            Close(commit: false);
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
        RenderPopup(screen, CalculateFrame(size, fieldBounds));
    }

    public DropdownSelectFrame CalculateFrame(
        ConsoleSize size,
        Rect fieldBounds)
    {
        if (!IsOpen)
            return new DropdownSelectFrame(size, fieldBounds, null, null, null, 0, _list.CalculateFrameState(1));

        Rect bounds = PopupBounds(size, fieldBounds);
        Rect contentBounds = PopupRenderer.GetContentBounds(bounds, drawBorder: true);
        int contentRows = contentBounds.Height;
        Rect? scrollbarBounds = contentRows > 0 && _list.Count > Math.Max(1, contentRows)
            ? new Rect(bounds.Right - 1, contentBounds.Y, 1, contentRows)
            : null;
        return new DropdownSelectFrame(
            size,
            fieldBounds,
            bounds,
            contentBounds,
            scrollbarBounds,
            contentRows,
            _list.CalculateFrameState(contentRows));
    }

    public void RenderPopup(
        ScreenRenderer screen,
        DropdownSelectFrame frame)
    {
        if (!IsOpen)
            return;

        if (frame.PopupBounds is not { } bounds || frame.ContentBounds is not { } contentBounds)
            return;

        var palette = UiTheme.Current;

        var scrollState = frame.ContentRows > 0 ? _list.GetScrollState(frame.ContentRows, frame.ListState.ScrollTop) : null;
        var options = PaletteStyles.DialogPopupOptions(palette) with
        {
            DrawDoubleBorder = false,
            VerticalScrollState = scrollState,
        };
        var normalStyle = PaletteStyles.DialogFill(palette);
        var selectedStyle = PaletteStyles.InputHighlight(palette);

        new PopupRenderer().RenderPopup(screen, bounds, options, (_, renderedContentBounds) =>
        {
            _list.Render(screen, renderedContentBounds, frame.ListState, normalStyle, selectedStyle, normalStyle);
        });
    }

    public bool TryHandleFieldMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        Rect fieldBounds)
        => TryHandleFieldMouse(mouse, CalculateFrame(size, fieldBounds));

    public bool TryHandleFieldMouse(
        MouseConsoleInputEvent mouse,
        DropdownSelectFrame frame)
    {
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind != MouseEventKind.Down ||
            mouse.Y != frame.FieldBounds.Y ||
            mouse.X < frame.FieldBounds.X ||
            mouse.X >= frame.FieldBounds.Right)
        {
            return false;
        }

        Toggle(frame.Size, frame.FieldBounds);
        return true;
    }

    public bool TryHandlePopupMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        Rect fieldBounds,
        out bool selected)
        => TryHandlePopupMouse(mouse, CalculateFrame(size, fieldBounds), out selected);

    public bool TryHandlePopupMouse(
        MouseConsoleInputEvent mouse,
        DropdownSelectFrame frame,
        out bool selected)
    {
        selected = false;
        if (!IsOpen)
            return false;

        if (frame.PopupBounds is not { } bounds || frame.ContentBounds is not { } contentBounds)
            return false;
        ApplyCommittedFrame(frame);

        if (mouse.Kind == MouseEventKind.Down && mouse.Button == MouseButton.Left &&
            (mouse.X < bounds.X || mouse.X >= bounds.Right || mouse.Y < bounds.Y || mouse.Y >= bounds.Bottom))
        {
            Close();
            return true;
        }

        var listInput = _list.HandleMouse(
            mouse,
            contentBounds,
            frame.ScrollbarBounds,
            frame.ContentRows,
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
            Close(commit: true);
        }
        return true;
    }

    public bool TryHandleKey(ConsoleKeyInfo key, ConsoleSize size, Rect fieldBounds, out bool selected)
        => TryHandleKey(key, CalculateFrame(size, fieldBounds), out selected);

    public bool TryHandleKey(ConsoleKeyInfo key, DropdownSelectFrame frame, out bool selected)
    {
        selected = false;
        if (!IsOpen)
        {
            if (key.Key is ConsoleKey.DownArrow or ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Spacebar)
            {
                Open(frame.Size, frame.FieldBounds);
                return true;
            }

            return false;
        }

        ApplyCommittedFrame(frame);
        int contentRows = frame.ContentRows;
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                Close();
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                selected = true;
                Close(commit: true);
                return true;
        }

        return _list.HandleKey(key, contentRows).IsHandled;
    }

    public void ApplyCommittedFrame(DropdownSelectFrame frame)
    {
        if (!IsOpen)
            return;

        SelectedIndex = frame.ListState.SelectedIndex;
        ScrollTop = frame.ListState.ScrollTop;
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

public readonly record struct DropdownSelectFrame(
    ConsoleSize Size,
    Rect FieldBounds,
    Rect? PopupBounds,
    Rect? ContentBounds,
    Rect? ScrollbarBounds,
    int ContentRows,
    ScrollableListFrameState ListState);
