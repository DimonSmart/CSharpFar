using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class ModuleHelpDialog
{
    private readonly ModalDialogHost _modalDialogs;
    private readonly ScreenRenderer _screen;

    public ModuleHelpDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
        _screen = modalDialogs.Screen;
    }

    public void Show(string title, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        int scrollTop = 0;
        ScrollBarDragState? scrollbarDrag = null;
        ConsoleSize size = default;
        int contentHeight = 0;
        using var session = _modalDialogs.Open(context =>
        {
            size = context.Size;
            contentHeight = Math.Max(1, size.Height - 2);
            scrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, lines.Count, contentHeight);
            Draw(title, lines, scrollTop, size, contentHeight);
            _screen.SetCursorVisible(false);
        });

        while (true)
        {
            session.Render();
            var input = session.ReadInput();
            switch (input)
            {
                case KeyConsoleInputEvent key:
                    if (HandleKey(key.Key, lines.Count, contentHeight, ref scrollTop))
                        return;
                    break;
                case MouseConsoleInputEvent mouse:
                    HandleMouse(mouse, lines.Count, contentHeight, size, ref scrollTop, ref scrollbarDrag);
                    break;
            }
        }
    }

    private void Draw(
        string title,
        IReadOnlyList<string> lines,
        int scrollTop,
        ConsoleSize size,
        int contentHeight)
    {
        var palette = UiTheme.Current;
        var headerStyle = PaletteStyles.PathHeaderActive(palette);
        string position = lines.Count == 0 ? " 0/0 " : $" {scrollTop + 1}/{lines.Count} ";
        int titleWidth = Math.Max(0, size.Width - position.Length);
        _screen.Write(0, 0, Truncate(" " + title + " ", titleWidth).PadRight(titleWidth) + position, headerStyle);

        var bodyStyle = PaletteStyles.HelpBody(palette);
        int contentWidth = Math.Max(0, size.Width - 1);
        for (int row = 0; row < contentHeight; row++)
        {
            int lineIndex = scrollTop + row;
            string text = lineIndex < lines.Count ? lines[lineIndex] : string.Empty;
            _screen.Write(0, row + 1, Truncate(text, contentWidth).PadRight(contentWidth), bodyStyle);
        }

        if (lines.Count > contentHeight)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                _screen,
                new Rect(size.Width - 1, 1, 1, contentHeight),
                new ScrollState
                {
                    TotalItems = lines.Count,
                    ViewportItems = contentHeight,
                    FirstVisibleIndex = scrollTop,
                },
                new ScrollBarOptions
                {
                    Enabled = true,
                    DrawWhenNotScrollable = false,
                },
                PaletteStyles.DialogBorder(palette));
        }

        string footer = "Esc/F10 Close";
        _screen.Write(0, size.Height - 1, footer.PadRight(size.Width), PaletteStyles.KeyBarLabel(palette));
    }

    private static bool HandleKey(ConsoleKeyInfo key, int lineCount, int contentHeight, ref int scrollTop)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.F1:
            case ConsoleKey.F10:
                return true;
            case ConsoleKey.UpArrow:
                scrollTop = Math.Max(0, scrollTop - 1);
                return false;
            case ConsoleKey.DownArrow:
                scrollTop = Math.Min(Math.Max(0, lineCount - contentHeight), scrollTop + 1);
                return false;
            case ConsoleKey.PageUp:
                scrollTop = Math.Max(0, scrollTop - contentHeight);
                return false;
            case ConsoleKey.PageDown:
                scrollTop = Math.Min(Math.Max(0, lineCount - contentHeight), scrollTop + contentHeight);
                return false;
            case ConsoleKey.Home:
                scrollTop = 0;
                return false;
            case ConsoleKey.End:
                scrollTop = Math.Max(0, lineCount - contentHeight);
                return false;
            default:
                return false;
        }
    }

    private static bool HandleMouse(
        MouseConsoleInputEvent mouse,
        int lineCount,
        int contentHeight,
        ConsoleSize size,
        ref int scrollTop,
        ref ScrollBarDragState? scrollbarDrag)
    {
        if (mouse.Button == MouseButton.WheelUp && mouse.Kind == MouseEventKind.Wheel)
        {
            scrollTop = Math.Max(0, scrollTop - 3);
            return true;
        }

        if (mouse.Button == MouseButton.WheelDown && mouse.Kind == MouseEventKind.Wheel)
        {
            scrollTop = Math.Min(Math.Max(0, lineCount - contentHeight), scrollTop + 3);
            return true;
        }

        return ScrollBarMouseHandler.TryHandleMouse(
            mouse,
            new Rect(size.Width - 1, 1, 1, contentHeight),
            lineCount,
            contentHeight,
            ref scrollTop,
            ref scrollbarDrag);
    }

    private static string Truncate(string value, int width)
    {
        if (width <= 0)
            return string.Empty;

        return value.Length <= width ? value : value[..width];
    }
}
