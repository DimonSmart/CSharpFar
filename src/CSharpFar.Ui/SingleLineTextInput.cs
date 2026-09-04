using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public enum TextInputKeyResult
{
    Ignored,
    Handled,
    TextChanged,
    AcceptCurrentText,
}

internal readonly record struct SingleLineTextHistorySnapshot(int ItemCount, int SelectedIndex, int FirstVisibleIndex);

internal sealed class SingleLineTextHistoryFrame
{
    private SingleLineTextHistoryFrame(Rect popupBounds, Rect contentBounds, int visibleRows, SingleLineTextHistorySnapshot snapshot, int matchSetVersion, VerticalScrollbarFrame? scrollbarFrame)
    {
        if (snapshot.ItemCount <= 0 || visibleRows <= 0 || visibleRows > snapshot.ItemCount)
            throw new ArgumentOutOfRangeException(nameof(snapshot));
        if (snapshot.SelectedIndex < 0 || snapshot.SelectedIndex >= snapshot.ItemCount || snapshot.FirstVisibleIndex < 0 || snapshot.FirstVisibleIndex > snapshot.ItemCount - visibleRows)
            throw new ArgumentOutOfRangeException(nameof(snapshot));
        if (popupBounds.Width <= 0 || popupBounds.Height != visibleRows + 2 || contentBounds.Width < 0 || contentBounds.Height != visibleRows)
            throw new ArgumentException("History popup bounds are inconsistent.");
        if (scrollbarFrame is { } scrollbar && !scrollbar.Bounds.Equals(new Rect(popupBounds.Right - 1, contentBounds.Y, 1, contentBounds.Height)))
            throw new ArgumentException("History scrollbar geometry is inconsistent.", nameof(scrollbarFrame));

        PopupBounds = popupBounds; ContentBounds = contentBounds; VisibleRows = visibleRows;
        Snapshot = snapshot; MatchSetVersion = matchSetVersion; VerticalScrollbarFrame = scrollbarFrame;
    }

    public Rect PopupBounds { get; }
    public Rect ContentBounds { get; }
    public Rect? ScrollbarBounds => VerticalScrollbarFrame?.Bounds;
    public int VisibleRows { get; }
    public SingleLineTextHistorySnapshot Snapshot { get; }
    public int FirstVisibleIndex => Snapshot.FirstVisibleIndex;
    public int SelectedIndex => Snapshot.SelectedIndex;
    public int MatchSetVersion { get; }
    public VerticalScrollbarFrame? VerticalScrollbarFrame { get; }

    internal static SingleLineTextHistoryFrame Create(Rect popupBounds, Rect contentBounds, int visibleRows, SingleLineTextHistorySnapshot snapshot, int matchSetVersion, VerticalScrollbarFrame? scrollbarFrame) =>
        new(popupBounds, contentBounds, visibleRows, snapshot, matchSetVersion, scrollbarFrame);
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
                buffer.InsertText(text);
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
                    return history.AcceptSelected(buffer) switch
                    {
                        SingleLineTextHistoryAcceptResult.CurrentText => TextInputKeyResult.AcceptCurrentText,
                        SingleLineTextHistoryAcceptResult.HistoryItem => TextInputKeyResult.TextChanged,
                        _ => TextInputKeyResult.Ignored,
                    };
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
        IUiCanvas screen,
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
        string displayText = maskInput ? new string('*', buffer.Text.Length) : DisplayText(buffer.Text);
        string visible = displayText.Length > visibleStart ? displayText[visibleStart..] : string.Empty;
        string padded = ConsoleTextMetrics.FitToCells(visible, width);
        if (!buffer.HasSelection)
        {
            screen.Write(x, y, padded, normalStyle);
            return;
        }

        int selectionStart = buffer.SelectionStart!.Value;
        int selectionEnd = selectionStart + buffer.SelectionLength;
        int cell = 0;
        foreach (var rune in padded.EnumerateRunes())
        {
            int sourceStart = visibleStart + ConsoleTextMetrics.Utf16IndexFromCellOffset(padded, cell);
            bool isSelected = sourceStart >= selectionStart && sourceStart < selectionEnd;
            screen.Write(x + cell, y, rune.ToString(), isSelected ? selectedStyle : normalStyle);
            cell += ConsoleTextMetrics.GetCellWidth(rune);
        }
    }

    public static void Render(
        IUiCanvas screen,
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
            screen.WriteChar(x, y, HistoryDropdownArrow, history.History.HasItems ? normalStyle : DialogStyles.DisabledControl(normalStyle));
            return;
        }

        Render(screen, x, y, width - 1, buffer, normalStyle, selectedStyle, maskInput);
        screen.WriteChar(x + width - 1, y, HistoryDropdownArrow, history.History.HasItems ? normalStyle : DialogStyles.DisabledControl(normalStyle));
        if (renderDropdown)
            RenderHistoryDropdown(screen, x, y, width, history);
    }

    public static bool TryOpenHistoryDropdown(
        SingleLineTextHistoryState history,
        int fieldY,
        int screenHeight)
    {
        if (!history.History.HasItems)
            return false;
        int availableContentRows = AvailableDropdownContentRows(fieldY, screenHeight);
        return history.OpenAll(availableContentRows);
    }

    public static bool IsHistoryArrowHit(int fieldX, int fieldWidth, int fieldY, int mouseX, int mouseY) =>
        fieldWidth > 0 &&
        mouseY == fieldY &&
        mouseX == fieldX + fieldWidth - 1;

    internal static bool TryHandleHistoryPopupContentMouse(
        SingleLineTextHistoryState history,
        CommandLineState buffer,
        MouseConsoleInputEvent mouse,
        SingleLineTextHistoryFrame frame)
    {
        if (!history.IsDropdownOpen || history.Matches.Count == 0)
            return false;

        if (mouse.Kind == MouseEventKind.Wheel && frame.PopupBounds.Contains(mouse.X, mouse.Y))
        {
            int delta = mouse.Button switch
            {
                MouseButton.WheelUp => -1,
                MouseButton.WheelDown => 1,
                _ => 0,
            };
            if (delta == 0)
                return false;

            history.MoveSelection(delta, frame.VisibleRows);
            return true;
        }

        if (mouse.Button == MouseButton.Left &&
            mouse.Kind is MouseEventKind.Down or MouseEventKind.DoubleClick &&
            mouse.X >= frame.ContentBounds.X &&
            mouse.X < frame.ContentBounds.Right &&
            mouse.Y >= frame.ContentBounds.Y &&
            mouse.Y < frame.ContentBounds.Bottom)
        {
            int itemIndex = frame.FirstVisibleIndex + mouse.Y - frame.ContentBounds.Y;
            if (!history.Select(itemIndex, frame.VisibleRows))
                return false;

            _ = history.AcceptSelected(buffer);
            return true;
        }

        if (mouse.Button == MouseButton.Left &&
            mouse.Kind == MouseEventKind.Down &&
            (mouse.X < frame.PopupBounds.X || mouse.X >= frame.PopupBounds.Right || mouse.Y < frame.PopupBounds.Y || mouse.Y >= frame.PopupBounds.Bottom))
        {
            history.Close();
            return true;
        }

        return false;
    }

    internal static bool TryHandleHistoryScrollbarMouse(
        SingleLineTextHistoryState history,
        MouseConsoleInputEvent mouse,
        SingleLineTextHistoryFrame frame)
    {
        if (!history.IsDropdownOpen || frame.VerticalScrollbarFrame is not { } scrollbarFrame)
            return false;

        if (mouse.Kind == MouseEventKind.Wheel)
        {
            int delta = mouse.Button switch
            {
                MouseButton.WheelUp => -1,
                MouseButton.WheelDown => 1,
                _ => 0,
            };
            if (delta == 0)
                return false;

            history.MoveSelection(delta, frame.VisibleRows);
            return true;
        }

        VerticalScrollbarInputResult result = history.Scrollbar.HandleMouse(mouse, scrollbarFrame);
        if (!result.IsHandled)
            return false;

        if (result.PositionChanged)
            history.SetFirstVisibleIndex(result.FirstVisibleIndex, frame.VisibleRows);
        return true;
    }

    public static int GetCursorX(int x, int width, CommandLineState buffer)
    {
        int visibleStart = GetVisibleStart(buffer, width);
        return x + ConsoleTextMetrics.CellOffsetFromUtf16Index(buffer.Text, buffer.CursorPosition) -
            ConsoleTextMetrics.CellOffsetFromUtf16Index(buffer.Text, visibleStart);
    }

    public static string VisibleText(CommandLineState buffer, int width)
    {
        if (width <= 0)
            return string.Empty;

        int visibleStart = GetVisibleStart(buffer, width);
        string visible = DisplayText(buffer.Text.Length > visibleStart ? buffer.Text[visibleStart..] : string.Empty);
        return ConsoleTextMetrics.TruncateToCells(visible, width);
    }

    private static int GetVisibleStart(CommandLineState buffer, int width)
    {
        int cursorCell = ConsoleTextMetrics.CellOffsetFromUtf16Index(buffer.Text, buffer.CursorPosition);
        return ConsoleTextMetrics.Utf16IndexFromCellOffset(buffer.Text, Math.Max(0, cursorCell - Math.Max(0, width - 1)));
    }

    public static int AvailableDropdownContentRows(int fieldY, int screenHeight)
    {
        int availableFrameRowsBelowField = screenHeight - fieldY - 1;
        return Math.Max(0, availableFrameRowsBelowField - 2);
    }

    public static void RenderHistoryDropdown(
        IUiCanvas screen,
        int fieldX,
        int fieldY,
        int fieldWidth,
        SingleLineTextHistoryState history,
        int? screenHeight = null)
    {
        int effectiveScreenHeight = screenHeight
            ?? (screen.Size.Height > 0 ? screen.Size.Height : screen.Size.Height);
        var frame = CalculateHistoryDropdownFrame(fieldX, fieldY, fieldWidth, effectiveScreenHeight, history);
        if (frame is not { } value)
            return;

        RenderHistoryDropdown(screen, history, value);
    }

    internal static SingleLineTextHistoryFrame? CalculateHistoryDropdownFrame(
        int fieldX,
        int fieldY,
        int fieldWidth,
        int screenHeight,
        SingleLineTextHistoryState history)
    {
        if (!history.IsDropdownOpen || history.Matches.Count == 0 || fieldWidth <= 0)
            return null;

        int availableContentRows = AvailableDropdownContentRows(fieldY, screenHeight);
        int visibleRows = history.VisibleRows(availableContentRows);
        if (visibleRows <= 0)
            return null;

        int firstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            history.FirstVisibleIndex,
            history.Matches.Count,
            visibleRows);
        int selectedIndex = Math.Clamp(history.SelectedIndex, 0, history.Matches.Count - 1);
        firstVisibleIndex = ScrollStateCalculator.EnsureIndexVisible(selectedIndex, firstVisibleIndex, visibleRows);
        firstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(firstVisibleIndex, history.Matches.Count, visibleRows);
        var bounds = new Rect(fieldX, fieldY + 1, fieldWidth, visibleRows + 2);
        var contentBounds = new Rect(
            bounds.X + 1,
            bounds.Y + 1,
            Math.Max(0, bounds.Width - 2),
            Math.Max(0, bounds.Height - 2));
        Rect? scrollbarBounds = history.Matches.Count > visibleRows
            ? new Rect(bounds.Right - 1, contentBounds.Y, 1, contentBounds.Height)
            : null;
        var scrollbarFrame = history.Scrollbar.CalculateFrame(scrollbarBounds, new ScrollState
        {
            TotalItems = history.Matches.Count,
            ViewportItems = visibleRows,
            FirstVisibleIndex = firstVisibleIndex,
        });
        return SingleLineTextHistoryFrame.Create(bounds, contentBounds, visibleRows,
            new SingleLineTextHistorySnapshot(history.Matches.Count, selectedIndex, firstVisibleIndex), history.MatchSetVersion, scrollbarFrame);
    }

    internal static void RenderHistoryDropdown(
        IUiCanvas screen,
        SingleLineTextHistoryState history,
        SingleLineTextHistoryFrame frame)
    {
        var palette = UiTheme.Current;
        var popupOptions = PaletteStyles.DialogPopupOptions(palette) with
        {
            DrawShadow = false,
            VerticalScrollState = new ScrollState
            {
                TotalItems = history.Matches.Count,
                ViewportItems = frame.VisibleRows,
                FirstVisibleIndex = frame.FirstVisibleIndex,
            },
        };
        var normalStyle = PaletteStyles.DialogFill(palette);
        var selectedStyle = PaletteStyles.InputField(palette);

        new PopupRenderer().RenderPopup(screen, frame.PopupBounds, popupOptions, (_, contentBounds) =>
        {
            for (int row = 0; row < frame.VisibleRows; row++)
            {
                int itemIndex = frame.FirstVisibleIndex + row;
                string text = Fit(history.Matches[itemIndex], contentBounds.Width);
                CellStyle style = itemIndex == frame.SelectedIndex ? selectedStyle : normalStyle;
                screen.Write(contentBounds.X, contentBounds.Y + row, text, style);
            }
        });
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return ConsoleTextMetrics.FitToCells(DisplayText(text), width);
    }

    private static string DisplayText(string text) => text.Replace('\n', '↵');

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
