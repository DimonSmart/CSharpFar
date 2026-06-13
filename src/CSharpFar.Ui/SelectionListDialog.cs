using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed record SelectionListDialogResult<T>(
    bool IsConfirmed,
    T? SelectedItem,
    int SelectedIndex);

public sealed class SelectionListDialog<T>
{
    private const int DefaultMaxVisibleRows = 15;
    private const int DefaultMinWidth = 20;

    private readonly IReadOnlyList<T> _items;
    private readonly Func<T, string> _itemText;
    private readonly string _title;
    private readonly DialogFrameRenderer _frameRenderer = new();

    public SelectionListDialog(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        string title)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _itemText = itemText ?? throw new ArgumentNullException(nameof(itemText));
        _title = title ?? throw new ArgumentNullException(nameof(title));
    }

    public int SelectedIndex { get; set; }

    public int ScrollTop { get; set; }

    public int MaxVisibleRows { get; set; } = DefaultMaxVisibleRows;

    public int? MaxWidth { get; set; }

    public int? MaxHeight { get; set; }

    public string? EmptyText { get; set; }

    public bool DoubleBorder { get; set; }

    public Action<T, int>? SelectionChanged { get; set; }

    public Action? RenderUnderlay { get; set; }

    public SelectionListDialogResult<T> Show(ScreenRenderer screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var size = screen.GetSize();
        var saved = screen.Capture(new Rect(0, 0, size.Width, size.Height));
        ScrollBarDragState? scrollbarDrag = null;
        int previousSelection = -1;

        try
        {
            SelectedIndex = _items.Count == 0
                ? -1
                : Math.Clamp(SelectedIndex, 0, _items.Count - 1);
            while (true)
            {
                var layout = CalculateLayout(size);
                NormalizeSelection(layout.VisibleRows);
                if (_items.Count > 0 && SelectedIndex != previousSelection)
                {
                    SelectionChanged?.Invoke(_items[SelectedIndex], SelectedIndex);
                    previousSelection = SelectedIndex;
                }

                Draw(screen, layout);
                var input = screen.ReadInput();

                if (input is MouseConsoleInputEvent mouse &&
                    HandleMouse(mouse, layout, ref scrollbarDrag, out bool confirmed))
                {
                    if (confirmed)
                        return Confirmed();
                    continue;
                }

                if (input is not KeyConsoleInputEvent { Key: var key })
                    continue;

                if (HandleKey(key, layout.VisibleRows, out bool isConfirmed))
                    return isConfirmed && _items.Count > 0 ? Confirmed() : Cancelled();
            }
        }
        finally
        {
            screen.Restore(saved);
            screen.SetCursorVisible(false);
        }
    }

    private SelectionListDialogResult<T> Confirmed() =>
        new(true, _items[SelectedIndex], SelectedIndex);

    private static SelectionListDialogResult<T> Cancelled() =>
        new(false, default, -1);

    private bool HandleKey(ConsoleKeyInfo key, int visibleRows, out bool confirmed)
    {
        confirmed = false;
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.F10:
                return true;
            case ConsoleKey.Enter:
                confirmed = true;
                return true;
            case ConsoleKey.UpArrow:
                SelectedIndex = Math.Max(0, SelectedIndex - 1);
                return false;
            case ConsoleKey.DownArrow:
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + 1);
                return false;
            case ConsoleKey.PageUp:
                SelectedIndex = Math.Max(0, SelectedIndex - visibleRows);
                return false;
            case ConsoleKey.PageDown:
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + visibleRows);
                return false;
            case ConsoleKey.Home:
                SelectedIndex = 0;
                return false;
            case ConsoleKey.End:
                SelectedIndex = _items.Count - 1;
                return false;
            default:
                return false;
        }
    }

    private bool HandleMouse(
        MouseConsoleInputEvent mouse,
        SelectionListLayout layout,
        ref ScrollBarDragState? scrollbarDrag,
        out bool confirmed)
    {
        confirmed = false;

        if (mouse.Kind == MouseEventKind.Wheel)
        {
            if (mouse.Button == MouseButton.WheelUp)
                SelectedIndex = Math.Max(0, SelectedIndex - 1);
            else if (mouse.Button == MouseButton.WheelDown)
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + 1);
            else
                return false;

            return true;
        }

        if (_items.Count > layout.VisibleRows &&
            TryHandleScrollbarMouse(
                mouse,
                layout.ScrollbarBounds,
                _items.Count,
                layout.VisibleRows,
                ref scrollbarDrag))
        {
            return true;
        }

        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click or MouseEventKind.DoubleClick))
        {
            return false;
        }

        if (mouse.X < layout.ContentBounds.X ||
            mouse.X >= layout.ContentBounds.Right ||
            mouse.Y < layout.ContentBounds.Y ||
            mouse.Y >= layout.ContentBounds.Bottom)
        {
            return false;
        }

        int index = ScrollTop + mouse.Y - layout.ContentBounds.Y;
        if (index < 0 || index >= _items.Count)
            return false;

        SelectedIndex = index;
        confirmed = mouse.Kind is MouseEventKind.Click or MouseEventKind.DoubleClick;
        return true;
    }

    private bool TryHandleScrollbarMouse(
        MouseConsoleInputEvent mouse,
        Rect scrollbarBounds,
        int totalItems,
        int viewportItems,
        ref ScrollBarDragState? dragState)
    {
        int selectedIndex = SelectedIndex;
        int scrollTop = ScrollTop;
        if (!ScrollableListMouseHandler.TryHandleScrollbarMouse(
                mouse,
                scrollbarBounds,
                totalItems,
                viewportItems,
                ref selectedIndex,
                ref scrollTop,
                ref dragState))
        {
            return false;
        }

        SelectedIndex = selectedIndex;
        ScrollTop = scrollTop;
        return true;
    }

    private void Draw(ScreenRenderer screen, SelectionListLayout layout)
    {
        var palette = UiTheme.Current;
        using var frame = screen.BeginFrame();
        RenderUnderlay?.Invoke();

        var scrollState = _items.Count > layout.VisibleRows
            ? new ScrollState
            {
                TotalItems = _items.Count,
                ViewportItems = layout.VisibleRows,
                FirstVisibleIndex = ScrollTop,
            }
            : null;

        _frameRenderer.RenderFrame(
            screen,
            layout.Bounds,
            _title,
            DoubleBorder,
            PaletteStyles.DialogPopupOptions(palette),
            scrollState,
            (_, _) =>
            {
                for (int row = 0; row < layout.VisibleRows; row++)
                {
                    int index = ScrollTop + row;
                    string text = index < _items.Count
                        ? _itemText(_items[index])
                        : row == 0 && _items.Count == 0 && EmptyText is not null
                            ? EmptyText
                            : string.Empty;
                    var style = index == SelectedIndex
                        ? PaletteStyles.InputField(palette)
                        : PaletteStyles.DialogFill(palette);
                    screen.Write(
                        layout.ContentBounds.X,
                        layout.ContentBounds.Y + row,
                        Fit(text, layout.ContentBounds.Width),
                        style);
                }
            });

        screen.SetCursorVisible(false);
    }

    private SelectionListLayout CalculateLayout(ConsoleSize size)
    {
        int itemWidth = _items.Count == 0 ? EmptyText?.Length ?? 0 : _items.Max(item => _itemText(item).Length);
        int contentWidth = Math.Max(DefaultMinWidth, Math.Max(itemWidth, _title.Length) + 2);
        int maxWidth = MaxWidth.HasValue ? Math.Min(MaxWidth.Value, size.Width) : size.Width - 2;
        contentWidth = Math.Min(contentWidth, Math.Max(DefaultMinWidth, maxWidth - 2));

        int maxRows = Math.Max(1, Math.Min(MaxVisibleRows, MaxHeight.GetValueOrDefault(size.Height) - 2));
        int visibleRows = Math.Min(Math.Max(1, _items.Count == 0 ? 1 : _items.Count), Math.Max(1, Math.Min(maxRows, size.Height - 2)));
        int width = Math.Min(size.Width, contentWidth + 2);
        int height = Math.Min(size.Height, visibleRows + 2);
        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        var bounds = new Rect(x, y, width, height);
        var contentBounds = new Rect(x + 1, y + 1, Math.Max(1, width - 2), Math.Max(1, height - 2));
        return new SelectionListLayout(
            bounds,
            contentBounds,
            new Rect(bounds.Right - 1, contentBounds.Y, 1, contentBounds.Height),
            contentBounds.Height);
    }

    private void NormalizeSelection(int visibleRows)
    {
        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, _items.Count - 1));
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, _items.Count, visibleRows);
        ScrollTop = ScrollStateCalculator.EnsureIndexVisible(SelectedIndex, ScrollTop, visibleRows);
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, _items.Count, visibleRows);
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        return text.Length <= width ? text.PadRight(width) : text[..width];
    }

    private readonly record struct SelectionListLayout(
        Rect Bounds,
        Rect ContentBounds,
        Rect ScrollbarBounds,
        int VisibleRows);
}
