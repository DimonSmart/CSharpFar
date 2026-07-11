using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
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
    public const char HistoryDropdownArrow = '▼';

    public static TextInputKeyResult HandleKey(
        CommandLineState buffer,
        ConsoleKeyInfo key,
        ref string? error,
        ITextClipboard? clipboard = null)
    {
        clipboard ??= TextCopyTextClipboard.Instance;
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

        if (IsPlainControlV(key))
        {
            if (clipboard.TryGetText(out string text) && !string.IsNullOrEmpty(text))
            {
                string singleLine = text.ReplaceLineEndings(" ").Trim();
                buffer.InsertText(singleLine);
                error = null;
                return TextInputKeyResult.TextChanged;
            }
            return TextInputKeyResult.Handled;
        }

        if (IsPlainControlC(key))
        {
            string? selected = buffer.SelectedText;
            if (!string.IsNullOrEmpty(selected))
                clipboard.TrySetText(selected);
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
                if (HasControlWithoutAlt(key) && (key.Modifiers & ConsoleModifiers.Shift) != 0)
                    buffer.MoveToPreviousWordWithSelection();
                else if (HasControlWithoutAlt(key))
                    buffer.MoveToPreviousWord();
                else if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                    buffer.MoveCursorWithSelection(buffer.CursorPosition - 1);
                else
                    buffer.MoveCursor(-1);
                return TextInputKeyResult.Handled;
            case ConsoleKey.RightArrow:
                if (HasControlWithoutAlt(key) && (key.Modifiers & ConsoleModifiers.Shift) != 0)
                    buffer.MoveToNextWordWithSelection();
                else if (HasControlWithoutAlt(key))
                    buffer.MoveToNextWord();
                else if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                    buffer.MoveCursorWithSelection(buffer.CursorPosition + 1);
                else
                    buffer.MoveCursor(+1);
                return TextInputKeyResult.Handled;
            case ConsoleKey.Home:
                if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                    buffer.MoveCursorWithSelection(0);
                else
                    buffer.MoveToStart();
                return TextInputKeyResult.Handled;
            case ConsoleKey.End:
                if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                    buffer.MoveCursorWithSelection(buffer.Text.Length);
                else
                    buffer.MoveToEnd();
                return TextInputKeyResult.Handled;
            default:
                return TextInputKeyResult.Ignored;
        }
    }

    public static TextInputKeyResult HandleKey(
        CommandLineState buffer,
        ConsoleKeyInfo key,
        ref string? error,
        SingleLineTextHistoryState? history,
        int availableDropdownContentRows,
        ITextClipboard? clipboard = null)
    {
        if (history is null)
            return HandleKey(buffer, key, ref error, clipboard);

        if (history.IsDropdownOpen)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    return history.MoveSelection(-1, availableDropdownContentRows)
                        ? TextInputKeyResult.Handled
                        : TextInputKeyResult.Ignored;
                case ConsoleKey.DownArrow:
                    return history.MoveSelection(+1, availableDropdownContentRows)
                        ? TextInputKeyResult.Handled
                        : TextInputKeyResult.Ignored;
                case ConsoleKey.Enter:
                    return history.AcceptSelected(buffer)
                        ? TextInputKeyResult.TextChanged
                        : TextInputKeyResult.Ignored;
                case ConsoleKey.Escape:
                    history.Close();
                    return TextInputKeyResult.Handled;
            }
        }

        TextInputKeyResult result = HandleKey(buffer, key, ref error, clipboard);
        if (result == TextInputKeyResult.TextChanged)
            history.OpenForPrefix(buffer.Text, availableDropdownContentRows);

        return result;
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

    public static void Render(
        ScreenRenderer screen,
        int x,
        int y,
        int width,
        CommandLineState buffer,
        CellStyle normalStyle,
        CellStyle selectedStyle,
        SingleLineTextHistoryState? history,
        bool maskInput = false,
        bool renderDropdown = true)
    {
        if (history is null)
        {
            Render(screen, x, y, width, buffer, normalStyle, selectedStyle, maskInput);
            return;
        }

        if (width <= 0)
            return;

        if (width == 1)
        {
            screen.WriteChar(x, y, HistoryDropdownArrow, normalStyle);
            return;
        }

        Render(screen, x, y, width - 1, buffer, normalStyle, selectedStyle, maskInput);
        screen.WriteChar(x + width - 1, y, HistoryDropdownArrow, normalStyle);
        if (renderDropdown)
            RenderHistoryDropdown(screen, x, y, width, history);
    }

    public static bool TryOpenHistoryDropdown(
        SingleLineTextHistoryState history,
        int fieldY,
        int screenHeight)
    {
        int availableContentRows = AvailableDropdownContentRows(fieldY, screenHeight);
        return history.OpenAll(availableContentRows);
    }

    public static bool IsHistoryArrowHit(int fieldX, int fieldWidth, int fieldY, int mouseX, int mouseY) =>
        fieldWidth > 0 &&
        mouseY == fieldY &&
        mouseX == fieldX + fieldWidth - 1;

    public static bool TryHandleHistoryDropdownMouse(
        SingleLineTextHistoryState history,
        CommandLineState buffer,
        MouseConsoleInputEvent mouse,
        int fieldX,
        int fieldY,
        int fieldWidth,
        int screenHeight,
        ref ScrollBarDragState? scrollbarDrag)
    {
        if (!history.IsDropdownOpen || history.Matches.Count == 0 || fieldWidth <= 0)
            return false;

        int availableRows = AvailableDropdownContentRows(fieldY, screenHeight);
        int visibleRows = history.VisibleRows(availableRows);
        if (visibleRows <= 0)
        {
            history.Close();
            return false;
        }

        var bounds = new Rect(fieldX, fieldY + 1, fieldWidth, visibleRows + 2);
        var contentBounds = new Rect(
            bounds.X + 1,
            bounds.Y + 1,
            Math.Max(0, bounds.Width - 2),
            Math.Max(0, bounds.Height - 2));

        int firstVisibleIndex = history.FirstVisibleIndex;
        if (ScrollBarMouseHandler.TryHandleMouse(
            mouse,
            new Rect(bounds.Right - 1, contentBounds.Y, 1, contentBounds.Height),
            history.Matches.Count,
            visibleRows,
            ref firstVisibleIndex,
            ref scrollbarDrag))
        {
            history.SetFirstVisibleIndex(firstVisibleIndex, availableRows);
            return true;
        }

        if (mouse.Button == MouseButton.Left &&
            mouse.Kind is MouseEventKind.Down or MouseEventKind.Click or MouseEventKind.DoubleClick &&
            mouse.X >= contentBounds.X &&
            mouse.X < contentBounds.Right &&
            mouse.Y >= contentBounds.Y &&
            mouse.Y < contentBounds.Bottom)
        {
            int itemIndex = history.FirstVisibleIndex + mouse.Y - contentBounds.Y;
            if (!history.Select(itemIndex, availableRows))
                return false;

            history.AcceptSelected(buffer);
            scrollbarDrag = null;
            return true;
        }

        if (mouse.Button == MouseButton.Left &&
            mouse.Kind is MouseEventKind.Down or MouseEventKind.Click &&
            (mouse.X < bounds.X || mouse.X >= bounds.Right || mouse.Y < bounds.Y || mouse.Y >= bounds.Bottom))
        {
            history.Close();
            scrollbarDrag = null;
            return true;
        }

        return false;
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

    public static int AvailableDropdownContentRows(int fieldY, int screenHeight)
    {
        int availableFrameRowsBelowField = screenHeight - fieldY - 1;
        return Math.Max(0, availableFrameRowsBelowField - 2);
    }

    public static void RenderHistoryDropdown(
        ScreenRenderer screen,
        int fieldX,
        int fieldY,
        int fieldWidth,
        SingleLineTextHistoryState history,
        int? screenHeight = null)
    {
        if (!history.IsDropdownOpen || history.Matches.Count == 0)
            return;

        int availableContentRows = AvailableDropdownContentRows(fieldY, screenHeight ?? screen.FrameViewport.Height);
        int visibleRows = history.VisibleRows(availableContentRows);
        if (visibleRows <= 0)
            return;

        var palette = UiTheme.Current;
        int scrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(
            history.FirstVisibleIndex,
            history.Matches.Count,
            visibleRows);
        var bounds = new Rect(fieldX, fieldY + 1, fieldWidth, visibleRows + 2);
        var popupOptions = PaletteStyles.DialogPopupOptions(palette) with
        {
            DrawShadow = false,
            VerticalScrollState = new ScrollState
            {
                TotalItems = history.Matches.Count,
                ViewportItems = visibleRows,
                FirstVisibleIndex = scrollTop,
            },
        };
        var normalStyle = PaletteStyles.DialogFill(palette);
        var selectedStyle = PaletteStyles.InputField(palette);

        new PopupRenderer().RenderPopup(screen, bounds, popupOptions, (_, contentBounds) =>
        {
            for (int row = 0; row < visibleRows; row++)
            {
                int itemIndex = scrollTop + row;
                string text = Fit(history.Matches[itemIndex], contentBounds.Width);
                CellStyle style = itemIndex == history.SelectedIndex ? selectedStyle : normalStyle;
                screen.Write(contentBounds.X, contentBounds.Y + row, text, style);
            }
        });
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length > width ? text[..width] : text.PadRight(width);
    }

    private static bool IsPlainControlA(ConsoleKeyInfo key)
    {
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

        return !hasAlt && !hasShift &&
            ((hasControl && key.Key == ConsoleKey.A) || key.KeyChar == '\u0001');
    }

    private static bool HasControlWithoutAlt(ConsoleKeyInfo key) =>
        (key.Modifiers & ConsoleModifiers.Control) != 0 &&
        (key.Modifiers & ConsoleModifiers.Alt) == 0;

    private static bool IsPlainControlV(ConsoleKeyInfo key)
    {
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

        return !hasAlt && !hasShift &&
            ((hasControl && key.Key == ConsoleKey.V) || key.KeyChar == '\u0016');
    }

    private static bool IsPlainControlC(ConsoleKeyInfo key)
    {
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

        return !hasAlt && !hasShift &&
            ((hasControl && key.Key == ConsoleKey.C) || key.KeyChar == '\u0003');
    }
}
