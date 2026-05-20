using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class FarNetMenuDialog
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public FarNetMenuDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public int? Show(string title, IReadOnlyList<string> items, int selected)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            return null;

        selected = Math.Clamp(selected, 0, items.Count - 1);
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        int firstVisibleIndex = 0;
        ScrollBarDragState? scrollbarDrag = null;
        try
        {
            _screen.SetCursorVisible(false);
            while (true)
            {
                var layout = CalculateLayout(title, items, size);
                firstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
                    Math.Clamp(firstVisibleIndex, selected - layout.VisibleRows + 1, selected),
                    items.Count,
                    layout.VisibleRows);

                Draw(title, items, selected, firstVisibleIndex, layout);
                var input = _screen.ReadInput();
                int? result;
                switch (input)
                {
                    case KeyConsoleInputEvent key:
                        if (HandleKey(key.Key, items.Count, ref selected, out result))
                            return result;
                        break;

                    case MouseConsoleInputEvent mouse:
                        if (HandleMouse(
                                mouse,
                                layout,
                                items.Count,
                                ref selected,
                                ref firstVisibleIndex,
                                ref scrollbarDrag,
                                out result))
                        {
                            if (result is not null)
                                return result;
                        }
                        break;
                }
            }
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private static bool HandleKey(ConsoleKeyInfo key, int itemCount, ref int selected, out int? result)
    {
        result = null;
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return true;
            case ConsoleKey.Enter:
                result = selected;
                return true;
            case ConsoleKey.UpArrow:
                selected = selected == 0 ? itemCount - 1 : selected - 1;
                return false;
            case ConsoleKey.DownArrow:
                selected = selected == itemCount - 1 ? 0 : selected + 1;
                return false;
            case ConsoleKey.PageUp:
                selected = Math.Max(0, selected - 10);
                return false;
            case ConsoleKey.PageDown:
                selected = Math.Min(itemCount - 1, selected + 10);
                return false;
            case ConsoleKey.Home:
                selected = 0;
                return false;
            case ConsoleKey.End:
                selected = itemCount - 1;
                return false;
            default:
                return false;
        }
    }

    private static bool HandleMouse(
        MouseConsoleInputEvent mouse,
        MenuLayout layout,
        int itemCount,
        ref int selected,
        ref int firstVisibleIndex,
        ref ScrollBarDragState? scrollbarDrag,
        out int? result)
    {
        result = null;

        if (mouse.Button == MouseButton.WheelUp && mouse.Kind == MouseEventKind.Wheel)
        {
            selected = Math.Max(0, selected - 1);
            return true;
        }

        if (mouse.Button == MouseButton.WheelDown && mouse.Kind == MouseEventKind.Wheel)
        {
            selected = Math.Min(itemCount - 1, selected + 1);
            return true;
        }

        if (ScrollableListMouseHandler.TryHandleScrollbarMouse(
                mouse,
                layout.ScrollbarBounds,
                itemCount,
                layout.VisibleRows,
                ref selected,
                ref firstVisibleIndex,
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

        int index = firstVisibleIndex + mouse.Y - layout.ContentBounds.Y;
        if (index < 0 || index >= itemCount)
            return false;

        selected = index;
        result = index;
        return true;
    }

    private static MenuLayout CalculateLayout(
        string title,
        IReadOnlyList<string> items,
        ConsoleSize size)
    {
        int contentWidth = Math.Min(
            Math.Max(items.Max(static item => item.Length), title.Length) + 2,
            Math.Max(20, size.Width - 4));
        int height = Math.Min(items.Count + 2, Math.Max(3, size.Height - 2));
        int x = Math.Max(0, (size.Width - contentWidth - 2) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        var bounds = new Rect(x, y, contentWidth + 2, height);
        var contentBounds = new Rect(x + 1, y + 1, contentWidth, Math.Max(1, height - 2));
        int visibleRows = contentBounds.Height;
        return new MenuLayout(
            bounds,
            contentBounds,
            new Rect(bounds.Right - 1, contentBounds.Y, 1, contentBounds.Height),
            visibleRows);
    }

    private void Draw(
        string title,
        IReadOnlyList<string> items,
        int selected,
        int firstVisibleIndex,
        MenuLayout layout)
    {
        var scrollState = items.Count > layout.VisibleRows
            ? new ScrollState
            {
                TotalItems = items.Count,
                ViewportItems = layout.VisibleRows,
                FirstVisibleIndex = firstVisibleIndex,
            }
            : null;

        new DialogFrameRenderer().RenderFrame(
            _screen,
            layout.Bounds,
            title,
            false,
            PaletteStyles.DialogPopupOptions(_palette),
            scrollState,
            (_, _) =>
            {
                for (int row = 0; row < layout.VisibleRows; row++)
                {
                    int index = firstVisibleIndex + row;
                    string text = index < items.Count ? items[index] : string.Empty;
                    var style = index == selected
                        ? PaletteStyles.InputField(_palette)
                        : PaletteStyles.DialogFill(_palette);
                    _screen.Write(
                        layout.ContentBounds.X,
                        layout.ContentBounds.Y + row,
                        Truncate(text, layout.ContentBounds.Width).PadRight(layout.ContentBounds.Width),
                        style);
                }
            });
    }

    private static string Truncate(string value, int width) =>
        value.Length <= width ? value : value[..Math.Max(0, width)];

    private readonly record struct MenuLayout(
        Rect Bounds,
        Rect ContentBounds,
        Rect ScrollbarBounds,
        int VisibleRows);
}
