using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class ModuleHelpDialog
{
    private readonly ModalDialogHost _modalDialogs;

    public ModuleHelpDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
    }

    public void Show(string title, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        int scrollTop = 0;
        ScrollBarDragState? scrollbarDrag = null;
        _modalDialogs.Run(
            context =>
            {
                var frame = CalculateFrame(context.Size, lines.Count, scrollTop);
                Draw(context.Screen, title, lines, frame);
                context.Screen.SetCursorVisible(false);
                return frame;
            },
            (input, frame) =>
            {
                switch (input)
                {
                    case KeyConsoleInputEvent key:
                        return HandleKey(key.Key, lines.Count, frame.ContentHeight, ref scrollTop)
                            ? ModalDialogLoopAction.Close
                            : ModalDialogLoopAction.Continue;
                    case MouseConsoleInputEvent mouse:
                        HandleMouse(mouse, lines.Count, frame, ref scrollTop, ref scrollbarDrag);
                        break;
                }

                return ModalDialogLoopAction.Continue;
            },
            applyCommittedFrame: frame => scrollTop = frame.ScrollTop);
    }

    private static ModuleHelpFrame CalculateFrame(ConsoleSize size, int lineCount, int requestedScrollTop)
    {
        int contentHeight = Math.Max(1, size.Height - 2);
        int scrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(requestedScrollTop, lineCount, contentHeight);
        Rect? scrollbarBounds = lineCount > contentHeight
            ? new Rect(Math.Max(0, size.Width - 1), 1, 1, contentHeight)
            : null;
        return new ModuleHelpFrame(size, contentHeight, scrollTop, scrollbarBounds);
    }

    private static void Draw(
        ScreenRenderer screen,
        string title,
        IReadOnlyList<string> lines,
        ModuleHelpFrame frame)
    {
        var palette = UiTheme.Current;
        var headerStyle = PaletteStyles.PathHeaderActive(palette);
        string position = lines.Count == 0 ? " 0/0 " : $" {frame.ScrollTop + 1}/{lines.Count} ";
        ConsoleSize size = frame.Size;
        int titleWidth = Math.Max(0, size.Width - position.Length);
        screen.Write(0, 0, Truncate(" " + title + " ", titleWidth).PadRight(titleWidth) + position, headerStyle);

        var bodyStyle = PaletteStyles.HelpBody(palette);
        int contentWidth = Math.Max(0, size.Width - 1);
        for (int row = 0; row < frame.ContentHeight; row++)
        {
            int lineIndex = frame.ScrollTop + row;
            string text = lineIndex < lines.Count ? lines[lineIndex] : string.Empty;
            screen.Write(0, row + 1, Truncate(text, contentWidth).PadRight(contentWidth), bodyStyle);
        }

        if (frame.ScrollbarBounds is { } scrollbarBounds)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                screen,
                scrollbarBounds,
                new ScrollState
                {
                    TotalItems = lines.Count,
                    ViewportItems = frame.ContentHeight,
                    FirstVisibleIndex = frame.ScrollTop,
                },
                new ScrollBarOptions
                {
                    Enabled = true,
                    DrawWhenNotScrollable = false,
                },
                PaletteStyles.DialogBorder(palette));
        }

        string footer = "Esc/F10 Close";
        screen.Write(0, size.Height - 1, footer.PadRight(size.Width), PaletteStyles.KeyBarLabel(palette));
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
        ModuleHelpFrame frame,
        ref int scrollTop,
        ref ScrollBarDragState? scrollbarDrag)
    {
        int contentHeight = frame.ContentHeight;
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

        return frame.ScrollbarBounds is { } scrollbarBounds &&
            ScrollBarMouseHandler.TryHandleMouse(
            mouse,
            scrollbarBounds,
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

    private readonly record struct ModuleHelpFrame(
        ConsoleSize Size,
        int ContentHeight,
        int ScrollTop,
        Rect? ScrollbarBounds);
}
