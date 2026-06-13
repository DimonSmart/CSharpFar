using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed record ListWithButtonsDialogResult<T>(
    string ActionId,
    T? SelectedItem,
    int SelectedIndex);

public sealed class ListWithButtonsDialog<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly Func<T, string> _itemText;
    private readonly DialogButtonBar _buttonBar;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public ListWithButtonsDialog(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        IReadOnlyList<DialogButton> buttons,
        string title)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _itemText = itemText ?? throw new ArgumentNullException(nameof(itemText));
        _buttonBar = new DialogButtonBar(buttons ?? throw new ArgumentNullException(nameof(buttons)));
        Title = title ?? throw new ArgumentNullException(nameof(title));
    }

    public string Title { get; }

    public int DialogWidth { get; set; } = 68;

    public int MinDialogWidth { get; set; } = 40;

    public int MaxVisibleRows { get; set; } = 12;

    public string? EmptyText { get; set; }

    public string DefaultListActionId { get; set; } = "default";

    public string CancelActionId { get; set; } = "cancel";

    public string? DeleteActionId { get; set; }

    public int SelectedIndex { get; set; }

    public int ScrollTop { get; set; }

    public ListWithButtonsDialogResult<T>? Show(ScreenRenderer screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var size = screen.GetSize();
        var saved = screen.Capture(new Rect(0, 0, size.Width, size.Height));
        int focusedButton = 0;
        bool focusButtons = _items.Count == 0;
        ScrollBarDragState? scrollbarDrag = null;
        screen.SetCursorVisible(false);

        try
        {
            while (true)
            {
                var layout = CalculateLayout(size);
                NormalizeSelection(layout.ListBounds.Height);
                focusedButton = Math.Clamp(focusedButton, 0, _buttonBar.Count - 1);
                Draw(screen, layout, focusButtons, focusedButton);

                var input = screen.ReadInput();
                if (input is MouseConsoleInputEvent mouse &&
                    HandleMouse(mouse, layout, ref focusedButton, ref focusButtons, ref scrollbarDrag, out var mouseResult))
                {
                    if (mouseResult is not null)
                        return mouseResult.ActionId == CancelActionId ? null : mouseResult;
                    continue;
                }

                if (input is not KeyConsoleInputEvent { Key: var key })
                    continue;

                if (focusButtons &&
                    _buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
                {
                    if (buttonId is not null)
                        return buttonId == CancelActionId ? null : CreateResult(buttonId);
                    continue;
                }

                if (HandleKey(key, layout.ListBounds.Height, ref focusButtons, out var keyResult))
                    return keyResult?.ActionId == CancelActionId ? null : keyResult;
            }
        }
        finally
        {
            screen.Restore(saved);
            screen.SetCursorVisible(false);
        }
    }

    private bool HandleKey(
        ConsoleKeyInfo key,
        int visibleRows,
        ref bool focusButtons,
        out ListWithButtonsDialogResult<T>? result)
    {
        result = null;
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.F10:
                result = CreateResult(CancelActionId);
                return true;
            case ConsoleKey.Tab:
                focusButtons = _items.Count == 0 || !focusButtons;
                return false;
            case ConsoleKey.Delete:
                if (DeleteActionId is not null && _items.Count > 0)
                {
                    result = CreateResult(DeleteActionId);
                    return true;
                }
                return false;
            case ConsoleKey.Enter:
                if (_items.Count > 0)
                {
                    result = CreateResult(DefaultListActionId);
                    return true;
                }
                focusButtons = true;
                return false;
            case ConsoleKey.UpArrow:
                if (_items.Count == 0) { focusButtons = true; return false; }
                focusButtons = false;
                SelectedIndex = Math.Max(0, SelectedIndex - 1);
                return false;
            case ConsoleKey.DownArrow:
                if (_items.Count == 0) { focusButtons = true; return false; }
                focusButtons = false;
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + 1);
                return false;
            case ConsoleKey.PageUp:
                if (_items.Count == 0) { focusButtons = true; return false; }
                focusButtons = false;
                SelectedIndex = Math.Max(0, SelectedIndex - visibleRows);
                return false;
            case ConsoleKey.PageDown:
                if (_items.Count == 0) { focusButtons = true; return false; }
                focusButtons = false;
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + visibleRows);
                return false;
            case ConsoleKey.Home:
                if (_items.Count == 0) { focusButtons = true; return false; }
                focusButtons = false;
                SelectedIndex = 0;
                return false;
            case ConsoleKey.End:
                if (_items.Count == 0) { focusButtons = true; return false; }
                focusButtons = false;
                SelectedIndex = _items.Count - 1;
                return false;
            default:
                return false;
        }
    }

    private bool HandleMouse(
        MouseConsoleInputEvent mouse,
        ListWithButtonsLayout layout,
        ref int focusedButton,
        ref bool focusButtons,
        ref ScrollBarDragState? scrollbarDrag,
        out ListWithButtonsDialogResult<T>? result)
    {
        result = null;

        if (_items.Count > layout.ListBounds.Height)
        {
            int selected = SelectedIndex;
            int scrollTop = ScrollTop;
            if (ScrollableListMouseHandler.TryHandleScrollbarMouse(
                    mouse,
                    new Rect(layout.FrameBounds.Right - 1, layout.ListBounds.Y, 1, layout.ListBounds.Height),
                    _items.Count,
                    layout.ListBounds.Height,
                    ref selected,
                    ref scrollTop,
                    ref scrollbarDrag))
            {
                SelectedIndex = selected;
                ScrollTop = scrollTop;
                focusButtons = false;
                return true;
            }
        }

        if (_buttonBar.TryHandleInput(mouse, ref focusedButton, out string? buttonId))
        {
            focusButtons = true;
            if (buttonId is not null)
                result = CreateResult(buttonId);
            return true;
        }

        if (_items.Count == 0 ||
            mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click or MouseEventKind.DoubleClick))
        {
            return false;
        }

        if (mouse.X < layout.ListBounds.X ||
            mouse.X >= layout.ListBounds.Right ||
            mouse.Y < layout.ListBounds.Y ||
            mouse.Y >= layout.ListBounds.Bottom)
        {
            return false;
        }

        int index = ScrollTop + mouse.Y - layout.ListBounds.Y;
        if (index < 0 || index >= _items.Count)
            return false;

        SelectedIndex = index;
        focusButtons = false;
        if (mouse.Kind == MouseEventKind.DoubleClick)
            result = CreateResult(DefaultListActionId);
        return true;
    }

    private ListWithButtonsDialogResult<T> CreateResult(string actionId)
    {
        if (_items.Count == 0 || SelectedIndex < 0 || SelectedIndex >= _items.Count)
            return new ListWithButtonsDialogResult<T>(actionId, default, -1);
        return new ListWithButtonsDialogResult<T>(actionId, _items[SelectedIndex], SelectedIndex);
    }

    private void Draw(ScreenRenderer screen, ListWithButtonsLayout layout, bool focusButtons, int focusedButton)
    {
        var scrollState = _items.Count > layout.ListBounds.Height
            ? new ScrollState
            {
                TotalItems = _items.Count,
                ViewportItems = layout.ListBounds.Height,
                FirstVisibleIndex = ScrollTop,
            }
            : null;

        using var frame = screen.BeginFrame();
        _modalRenderer.Render(screen, layout.Bounds, Title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, modalLayout) =>
        {
            if (scrollState is not null)
            {
                new ScrollBarRenderer().RenderVerticalScrollbar(
                    screen,
                    new Rect(modalLayout.FrameBounds.Right - 1, layout.ListBounds.Y, 1, layout.ListBounds.Height),
                    scrollState,
                    new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false },
                    FarDialogStyles.Border);
            }

            screen.FillRegion(layout.ListBounds, FarDialogStyles.Fill);
            if (_items.Count == 0)
            {
                screen.Write(layout.ListBounds.X, layout.ListBounds.Y, Fit(EmptyText ?? string.Empty, layout.ListBounds.Width), FarDialogStyles.Fill);
            }
            else
            {
                for (int row = 0; row < layout.ListBounds.Height; row++)
                {
                    int index = ScrollTop + row;
                    if (index >= _items.Count)
                        break;

                    var style = index == SelectedIndex ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill;
                    screen.Write(layout.ListBounds.X, layout.ListBounds.Y + row, Fit(_itemText(_items[index]), layout.ListBounds.Width), style);
                }
            }

            _buttonBar.Render(
                screen,
                layout.ListBounds.X,
                layout.ButtonY,
                layout.ListBounds.Width,
                focusedButton,
                FarDialogStyles.Fill,
                focusButtons ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill);
        });

        screen.SetCursorVisible(false);
    }

    private ListWithButtonsLayout CalculateLayout(ConsoleSize size)
    {
        int width = Math.Min(DialogWidth, Math.Max(MinDialogWidth, size.Width - 2));
        int targetListRows = Math.Min(MaxVisibleRows, Math.Max(1, _items.Count));
        int height = Math.Min(targetListRows + 7, Math.Max(8, size.Height - 2));
        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        var bounds = new Rect(x, y, width, height);
        var frameBounds = new Rect(bounds.X + 1, bounds.Y + 1, Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height - 2));
        var contentBounds = new Rect(bounds.X + 2, bounds.Y + 2, Math.Max(0, bounds.Width - 4), Math.Max(0, bounds.Height - 4));
        int buttonY = contentBounds.Bottom - 1;
        var listBounds = new Rect(contentBounds.X + 2, contentBounds.Y, Math.Max(1, contentBounds.Width - 4), Math.Max(1, buttonY - contentBounds.Y - 1));
        return new ListWithButtonsLayout(bounds, frameBounds, listBounds, buttonY);
    }

    private void NormalizeSelection(int visibleRows)
    {
        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, _items.Count - 1));
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, _items.Count, visibleRows);
        if (_items.Count > 0)
            ScrollTop = ScrollStateCalculator.EnsureIndexVisible(SelectedIndex, ScrollTop, visibleRows);
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, _items.Count, visibleRows);
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        return text.Length <= width ? text.PadRight(width) : text[..width];
    }

    private readonly record struct ListWithButtonsLayout(
        Rect Bounds,
        Rect FrameBounds,
        Rect ListBounds,
        int ButtonY);
}
