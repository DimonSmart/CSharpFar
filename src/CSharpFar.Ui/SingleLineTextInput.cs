using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public enum TextInputKeyResult
{
    Ignored,
    Handled,
    TextChanged,
}

public static class SingleLineTextInput
{
    public static TextInputKeyResult HandleKey(CommandLineState buffer, ConsoleKeyInfo key, ref string? error)
    {
        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

        if (isPrintable)
        {
            buffer.Insert(key.KeyChar);
            error = null;
            return TextInputKeyResult.TextChanged;
        }

        if (IsPlainControlA(key))
        {
            buffer.SelectAll();
            return TextInputKeyResult.Handled;
        }

        switch (key.Key)
        {
            case ConsoleKey.Backspace:
                buffer.DeleteBack();
                error = null;
                return TextInputKeyResult.TextChanged;
            case ConsoleKey.Delete:
                buffer.DeleteForward();
                error = null;
                return TextInputKeyResult.TextChanged;
            case ConsoleKey.LeftArrow:
                buffer.MoveCursor(-1);
                return TextInputKeyResult.Handled;
            case ConsoleKey.RightArrow:
                buffer.MoveCursor(+1);
                return TextInputKeyResult.Handled;
            case ConsoleKey.Home:
                buffer.MoveToStart();
                return TextInputKeyResult.Handled;
            case ConsoleKey.End:
                buffer.MoveToEnd();
                return TextInputKeyResult.Handled;
            default:
                return TextInputKeyResult.Ignored;
        }
    }

    public static void Render(
        ScreenRenderer screen,
        int x,
        int y,
        int width,
        CommandLineState buffer,
        CellStyle normalStyle,
        CellStyle selectedStyle,
        bool maskInput = false)
    {
        if (width <= 0)
            return;

        int visibleStart = GetVisibleStart(buffer, width);
        string displayText = maskInput ? new string('*', buffer.Text.Length) : buffer.Text;
        string visible = displayText.Length > visibleStart ? displayText[visibleStart..] : string.Empty;
        if (visible.Length > width)
            visible = visible[..width];

        string padded = visible.PadRight(width);
        if (!buffer.HasSelection)
        {
            screen.Write(x, y, padded, normalStyle);
            return;
        }

        int selectionStart = buffer.SelectionStart!.Value - visibleStart;
        int selectionEnd = selectionStart + buffer.SelectionLength;
        for (int i = 0; i < padded.Length; i++)
        {
            bool isSelected = i >= selectionStart && i < selectionEnd;
            screen.WriteChar(x + i, y, padded[i], isSelected ? selectedStyle : normalStyle);
        }
    }

    public static int GetCursorX(int x, int width, CommandLineState buffer)
    {
        int visibleStart = GetVisibleStart(buffer, width);
        return x + buffer.CursorPosition - visibleStart;
    }

    public static string VisibleText(CommandLineState buffer, int width)
    {
        if (width <= 0)
            return string.Empty;

        int visibleStart = GetVisibleStart(buffer, width);
        string visible = buffer.Text.Length > visibleStart ? buffer.Text[visibleStart..] : string.Empty;
        return visible.Length > width ? visible[..width] : visible;
    }

    private static int GetVisibleStart(CommandLineState buffer, int width) =>
        Math.Max(0, buffer.CursorPosition - Math.Max(0, width - 1));

    private static bool IsPlainControlA(ConsoleKeyInfo key)
    {
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

        return !hasAlt && !hasShift &&
            ((hasControl && key.Key == ConsoleKey.A) || key.KeyChar == '\u0001');
    }
}
