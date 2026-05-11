using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

internal sealed class SearchDialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 19;
    private const int ButtonRow = 10;
    private const int BodyRowCount = 13;

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();

    // Bounds of clickable option rows (updated each Draw call)
    private readonly Rect[] _optionBounds = new Rect[ButtonRow + 1];

    public SearchDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public SearchRequest? Show(string rootPath)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(size, rootPath);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    internal static SearchRequest? TryCreateRequest(
        string rootPath,
        string fileMaskExpression,
        string containingText,
        bool caseSensitive,
        bool wholeWords,
        bool notContaining,
        bool includeDirectoriesInResults,
        bool searchInSymbolicLinks,
        SearchScope scope,
        string maxDegreeOfParallelismText,
        out string? error)
    {
        string mask = string.IsNullOrWhiteSpace(fileMaskExpression)
            ? "*"
            : fileMaskExpression.Trim();

        if (!int.TryParse(maxDegreeOfParallelismText.Trim(), out int maxDegreeOfParallelism) ||
            maxDegreeOfParallelism is < 1 or > 16)
        {
            error = "Parallelism must be a number from 1 to 16.";
            return null;
        }

        string? text = containingText.Length == 0 ? null : containingText;
        error = null;
        return new SearchRequest
        {
            RootPath = rootPath,
            FileMaskExpression = mask,
            ContainingText = text,
            CaseSensitive = caseSensitive,
            WholeWords = wholeWords,
            NotContaining = text is not null && notContaining,
            IncludeDirectoriesInResults = includeDirectoriesInResults,
            SearchInSymbolicLinks = searchInSymbolicLinks,
            Scope = scope,
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
        };
    }

    internal static int DefaultParallelism() =>
        Math.Clamp(Math.Min(Environment.ProcessorCount, 4), 1, 16);

    private SearchRequest? RunLoop(ConsoleSize size, string rootPath)
    {
        var mask = new CommandLineState();
        mask.SetText("*.*");

        var text = new CommandLineState();
        var parallelism = new CommandLineState();
        parallelism.SetText(DefaultParallelism().ToString(System.Globalization.CultureInfo.InvariantCulture));

        bool caseSensitive = false;
        bool wholeWords = false;
        bool notContaining = false;
        bool includeDirectoriesInResults = false;
        bool searchInSymbolicLinks = false;
        var scope = SearchScope.CurrentDirectoryRecursive;
        int focusRow = 0;
        int bodyScrollTop = 0;
        ScrollBarDragState? bodyScrollbarDrag = null;
        int focusedButton = 0;
        string? error = null;
        var buttonBar = new DialogButtonBar(
        [
            new DialogButton("find", "Find", 'F', IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C'),
        ]);

        bodyScrollTop = NormalizeBodyScroll(size, focusRow, bodyScrollTop);
        Draw(
            size,
            mask,
            text,
            parallelism,
            caseSensitive,
            wholeWords,
            notContaining,
            includeDirectoriesInResults,
            searchInSymbolicLinks,
            scope,
            focusRow,
            bodyScrollTop,
            buttonBar,
            focusedButton,
            error);

        while (true)
        {
            var input = _screen.ReadInput();
            bool hasText = text.Text.Length > 0;

            if (!hasText && notContaining)
                notContaining = false;
            if (!IsFocusableRow(focusRow, hasText))
                focusRow = NextFocusableRow(focusRow, hasText);

            if (focusRow == ButtonRow &&
                buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                if (buttonId is not null)
                {
                    if (buttonId == "cancel")
                        return null;

                    var result = BuildRequest(
                        rootPath,
                        mask,
                        text,
                        caseSensitive,
                        wholeWords,
                        notContaining,
                        includeDirectoriesInResults,
                        searchInSymbolicLinks,
                        scope,
                        parallelism,
                        ref error);
                    if (result is not null)
                        return result;
                }

                DrawCurrent();
                continue;
            }

            if (input is MouseConsoleInputEvent mouse)
            {
                if (TryHandleBodyScrollbarMouse(mouse, size, ref bodyScrollTop, ref bodyScrollbarDrag))
                {
                    DrawCurrent(ensureFocusVisible: false);
                    continue;
                }

                // Check option row clicks first
                int clickedRow = HitTestOptionRow(mouse);
                if (clickedRow >= 0 &&
                    mouse.Button == MouseButton.Left &&
                    mouse.Kind is MouseEventKind.Down or MouseEventKind.Click)
                {
                    focusRow = clickedRow;
                    if (clickedRow is not (0 or 1 or 9))
                    {
                        CycleValue(
                            clickedRow,
                            hasText,
                            ref caseSensitive,
                            ref wholeWords,
                            ref notContaining,
                            ref includeDirectoriesInResults,
                            ref searchInSymbolicLinks,
                            ref scope);
                    }
                    DrawCurrent();
                    continue;
                }

                // Check button bar clicks
                if (buttonBar.TryHandleInput(input, ref focusedButton, out buttonId) &&
                    buttonId is not null)
                {
                    focusRow = ButtonRow;
                    if (buttonId == "cancel")
                        return null;

                    var result = BuildRequest(
                        rootPath,
                        mask,
                        text,
                        caseSensitive,
                        wholeWords,
                        notContaining,
                        includeDirectoriesInResults,
                        searchInSymbolicLinks,
                        scope,
                        parallelism,
                        ref error);
                    if (result is not null)
                        return result;
                }

                DrawCurrent();
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
            {
                DrawCurrent();
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return null;
                case ConsoleKey.F10:
                    var f10Result = BuildRequest(
                        rootPath,
                        mask,
                        text,
                        caseSensitive,
                        wholeWords,
                        notContaining,
                        includeDirectoriesInResults,
                        searchInSymbolicLinks,
                        scope,
                        parallelism,
                        ref error);
                    if (f10Result is not null)
                        return f10Result;
                    break;
                case ConsoleKey.Enter:
                    if (focusRow == ButtonRow && focusedButton == 1)
                        return null;
                    if (focusRow == ButtonRow || focusRow is 0 or 1 or 9)
                    {
                        var result = BuildRequest(
                            rootPath,
                            mask,
                            text,
                            caseSensitive,
                            wholeWords,
                            notContaining,
                            includeDirectoriesInResults,
                            searchInSymbolicLinks,
                            scope,
                            parallelism,
                            ref error);
                        if (result is not null)
                            return result;
                    }
                    else
                    {
                        CycleValue(
                            focusRow,
                            hasText,
                            ref caseSensitive,
                            ref wholeWords,
                            ref notContaining,
                            ref includeDirectoriesInResults,
                            ref searchInSymbolicLinks,
                            ref scope);
                    }
                    break;
                case ConsoleKey.Spacebar:
                    if (focusRow == ButtonRow)
                    {
                        if (focusedButton == 1)
                            return null;

                        var result = BuildRequest(
                            rootPath,
                            mask,
                            text,
                            caseSensitive,
                            wholeWords,
                            notContaining,
                            includeDirectoriesInResults,
                            searchInSymbolicLinks,
                            scope,
                            parallelism,
                            ref error);
                        if (result is not null)
                            return result;
                    }
                    else if (focusRow is 0 or 1 or 9)
                    {
                        EditText(CurrentBuffer(focusRow, mask, text, parallelism), key, ref error);
                    }
                    else
                    {
                        CycleValue(
                            focusRow,
                            hasText,
                            ref caseSensitive,
                            ref wholeWords,
                            ref notContaining,
                            ref includeDirectoriesInResults,
                            ref searchInSymbolicLinks,
                            ref scope);
                    }
                    break;
                case ConsoleKey.UpArrow:
                    focusRow = PreviousFocusableRow(focusRow, hasText);
                    break;
                case ConsoleKey.DownArrow:
                case ConsoleKey.Tab:
                    focusRow = NextFocusableRow(focusRow, hasText);
                    break;
                case ConsoleKey.LeftArrow:
                    if (focusRow == ButtonRow)
                        buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else if (focusRow is 0 or 1 or 9)
                        EditText(CurrentBuffer(focusRow, mask, text, parallelism), key, ref error);
                    break;
                case ConsoleKey.RightArrow:
                    if (focusRow == ButtonRow)
                        buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else if (focusRow is 0 or 1 or 9)
                        EditText(CurrentBuffer(focusRow, mask, text, parallelism), key, ref error);
                    break;
                default:
                    if (focusRow is 0 or 1 or 9)
                        EditText(CurrentBuffer(focusRow, mask, text, parallelism), key, ref error);
                    break;
            }

            DrawCurrent();
        }

        void DrawCurrent(bool ensureFocusVisible = true)
        {
            bodyScrollTop = ensureFocusVisible
                ? NormalizeBodyScroll(size, focusRow, bodyScrollTop)
                : ScrollStateCalculator.ClampFirstVisibleIndex(
                    bodyScrollTop,
                    BodyRowCount,
                    SearchBodyViewportRows(size));
            Draw(
                size,
                mask,
                text,
                parallelism,
                caseSensitive,
                wholeWords,
                notContaining,
                includeDirectoriesInResults,
                searchInSymbolicLinks,
                scope,
                focusRow,
                bodyScrollTop,
                buttonBar,
                focusedButton,
                error);
        }
    }

    private static bool TryHandleBodyScrollbarMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        ref int bodyScrollTop,
        ref ScrollBarDragState? bodyScrollbarDrag)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(48, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        var frameBounds = new Rect(
            dialogX + 1,
            dialogY + 1,
            Math.Max(1, dialogWidth - 2),
            Math.Max(1, dialogHeight - 2));
        int buttonY = frameBounds.Y + frameBounds.Height - 2;
        int errorY = buttonY - 1;
        int bodyTop = frameBounds.Y + 1;
        int bodyHeight = Math.Max(1, errorY - bodyTop);
        if (BodyRowCount <= bodyHeight)
            return false;

        var scrollbarBounds = new Rect(frameBounds.Right - 1, bodyTop, 1, bodyHeight);
        return ScrollBarMouseHandler.TryHandleMouse(
            mouse,
            scrollbarBounds,
            BodyRowCount,
            bodyHeight,
            ref bodyScrollTop,
            ref bodyScrollbarDrag);
    }

    private static SearchRequest? BuildRequest(
        string rootPath,
        CommandLineState mask,
        CommandLineState text,
        bool caseSensitive,
        bool wholeWords,
        bool notContaining,
        bool includeDirectoriesInResults,
        bool searchInSymbolicLinks,
        SearchScope scope,
        CommandLineState parallelism,
        ref string? error)
    {
        return TryCreateRequest(
            rootPath,
            mask.Text,
            text.Text,
            caseSensitive,
            wholeWords,
            notContaining,
            includeDirectoriesInResults,
            searchInSymbolicLinks,
            scope,
            parallelism.Text,
            out error);
    }

    private static CommandLineState CurrentBuffer(
        int focusRow,
        CommandLineState mask,
        CommandLineState text,
        CommandLineState parallelism) =>
        focusRow switch
        {
            0 => mask,
            1 => text,
            _ => parallelism,
        };

    private static void CycleValue(
        int focusRow,
        bool hasText,
        ref bool caseSensitive,
        ref bool wholeWords,
        ref bool notContaining,
        ref bool includeDirectoriesInResults,
        ref bool searchInSymbolicLinks,
        ref SearchScope scope)
    {
        switch (focusRow)
        {
            case 3:
                caseSensitive = !caseSensitive;
                break;
            case 4:
                wholeWords = !wholeWords;
                break;
            case 5 when hasText:
                notContaining = !notContaining;
                break;
            case 6:
                includeDirectoriesInResults = !includeDirectoriesInResults;
                break;
            case 7:
                searchInSymbolicLinks = !searchInSymbolicLinks;
                break;
            case 8:
                scope = scope == SearchScope.CurrentDirectoryOnly
                    ? SearchScope.CurrentDirectoryRecursive
                    : SearchScope.CurrentDirectoryOnly;
                break;
        }
    }

    private static int NextFocusableRow(int focusRow, bool hasText)
    {
        for (int i = 0; i <= ButtonRow; i++)
        {
            int next = focusRow >= ButtonRow ? 0 : focusRow + 1;
            if (IsFocusableRow(next, hasText))
                return next;
            focusRow = next;
        }

        return 0;
    }

    private static int PreviousFocusableRow(int focusRow, bool hasText)
    {
        for (int i = 0; i <= ButtonRow; i++)
        {
            int previous = focusRow <= 0 ? ButtonRow : focusRow - 1;
            if (IsFocusableRow(previous, hasText))
                return previous;
            focusRow = previous;
        }

        return 0;
    }

    private static bool IsFocusableRow(int row, bool hasText) =>
        row is 0 or 1 or 3 or 4 or 6 or 7 or 8 or 9 or ButtonRow ||
        (row == 5 && hasText);

    private static void EditText(CommandLineState buffer, ConsoleKeyInfo key, ref string? error)
    {
        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

        if (isPrintable)
        {
            buffer.Insert(key.KeyChar);
            error = null;
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Backspace: buffer.DeleteBack(); error = null; break;
            case ConsoleKey.Delete: buffer.DeleteForward(); error = null; break;
            case ConsoleKey.LeftArrow: buffer.MoveCursor(-1); break;
            case ConsoleKey.RightArrow: buffer.MoveCursor(+1); break;
            case ConsoleKey.Home: buffer.MoveToStart(); break;
            case ConsoleKey.End: buffer.MoveToEnd(); break;
        }
    }

    private void Draw(
        ConsoleSize size,
        CommandLineState mask,
        CommandLineState text,
        CommandLineState parallelism,
        bool caseSensitive,
        bool wholeWords,
        bool notContaining,
        bool includeDirectoriesInResults,
        bool searchInSymbolicLinks,
        SearchScope scope,
        int focusRow,
        int bodyScrollTop,
        DialogButtonBar buttonBar,
        int focusedButton,
        string? error)
    {
        using var frame = _screen.BeginFrame();

        int dialogWidth = Math.Min(DialogWidth, Math.Max(48, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        var outerBounds = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);

        var fill = FarDialogStyles.Fill;
        var focused = FarDialogStyles.FocusedInput;
        var disabled = new CellStyle(ConsoleColor.DarkGray, fill.Background);

        _modalRenderer.Render(_screen, outerBounds, "Find file", true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);

            Array.Clear(_optionBounds);
            int buttonY = bounds.Y + bounds.Height - 2;
            int errorY = buttonY - 1;
            int bodyTop = bounds.Y + 1;
            int bodyHeight = Math.Max(1, errorY - bodyTop);
            _screen.FillRegion(new Rect(contentX, bodyTop, contentWidth, bodyHeight), fill);

            WriteBodyRow(0, "A file mask or several file masks:", fill);
            DrawBodyInput(1, mask, focusRow == 0, focusRow: 0);
            WriteBodyRow(2, "Containing text:", fill);
            DrawBodyInput(3, text, focusRow == 1, focusRow: 1);
            DrawBodyValueRow(4, "Using code page:", "Automatic detection", false, fill, focused);
            DrawBodyCheckbox(5, "Case sensitive", caseSensitive, focusRow == 3, fill, focused, focusRow: 3);
            DrawBodyCheckbox(6, "Whole words", wholeWords, focusRow == 4, fill, focused, focusRow: 4);
            DrawBodyCheckbox(
                7,
                "Not containing",
                text.Text.Length > 0 && notContaining,
                focusRow == 5,
                text.Text.Length > 0 ? fill : disabled,
                focused,
                focusRow: 5);
            DrawBodyCheckbox(8, "Search folders", includeDirectoriesInResults, focusRow == 6, fill, focused, focusRow: 6);
            DrawBodyCheckbox(9, "Search in symbolic links", searchInSymbolicLinks, focusRow == 7, fill, focused, focusRow: 7);
            DrawBodyValueRow(10, "Select search area:", ScopeLabel(scope), focusRow == 8, fill, focused, focusRow: 8);
            WriteBodyRow(11, "Parallelism:", fill);
            DrawBodyInput(12, parallelism, focusRow == 9, Math.Min(8, contentWidth), focusRow: 9);

            if (BodyRowCount > bodyHeight)
            {
                new ScrollBarRenderer().RenderVerticalScrollbar(
                    _screen,
                    new Rect(bounds.Right - 1, bodyTop, 1, bodyHeight),
                    new ScrollState
                    {
                        TotalItems = BodyRowCount,
                        ViewportItems = bodyHeight,
                        FirstVisibleIndex = bodyScrollTop,
                    },
                    new ScrollBarOptions
                    {
                        Enabled = true,
                        DrawWhenNotScrollable = false,
                    },
                    FarDialogStyles.Border);
            }

            string errorText = error is null ? string.Empty : Truncate(error, contentWidth);
            _screen.Write(contentX, errorY, errorText.PadRight(contentWidth), FarDialogStyles.Error);

            buttonBar.Render(
                _screen,
                contentX,
                buttonY,
                contentWidth,
                focusedButton,
                fill,
                focusRow == ButtonRow ? focused : fill);

            int? BodyY(int virtualRow)
            {
                int row = virtualRow - bodyScrollTop;
                return row >= 0 && row < bodyHeight ? bodyTop + row : null;
            }

            void WriteBodyRow(int virtualRow, string value, CellStyle style)
            {
                if (BodyY(virtualRow) is { } y)
                    _screen.Write(contentX, y, Truncate(value, contentWidth).PadRight(contentWidth), style);
            }

            void DrawBodyInput(int virtualRow, CommandLineState buffer, bool isFocused, int? width = null, int focusRow = -1)
            {
                if (BodyY(virtualRow) is not { } y)
                    return;

                int inputWidth = width ?? contentWidth;
                DrawInput(contentX, y, inputWidth, buffer, isFocused);
                if (focusRow >= 0)
                    _optionBounds[focusRow] = new Rect(contentX, y, inputWidth, 1);
            }

            void DrawBodyValueRow(
                int virtualRow,
                string label,
                string value,
                bool isFocused,
                CellStyle rowFill,
                CellStyle rowFocused,
                int focusRow = -1)
            {
                if (BodyY(virtualRow) is not { } y)
                    return;

                DrawValueRow(contentX, y, contentWidth, label, value, isFocused, rowFill, rowFocused);
                if (focusRow >= 0)
                    _optionBounds[focusRow] = new Rect(contentX, y, contentWidth, 1);
            }

            void DrawBodyCheckbox(
                int virtualRow,
                string label,
                bool value,
                bool isFocused,
                CellStyle rowFill,
                CellStyle rowFocused,
                int focusRow)
            {
                if (BodyY(virtualRow) is not { } y)
                    return;

                DrawCheckbox(contentX, y, contentWidth, label, value, isFocused, rowFill, rowFocused);
                _optionBounds[focusRow] = new Rect(contentX, y, contentWidth, 1);
            }
        });

        Rect frameBounds = new(
            outerBounds.X + 1,
            outerBounds.Y + 1,
            Math.Max(1, outerBounds.Width - 2),
            Math.Max(1, outerBounds.Height - 2));
        int inputX = frameBounds.X + 2;
        int inputWidth = Math.Max(1, frameBounds.Width - 4);
        if (focusRow == 0 && InputCursorY(frameBounds, bodyScrollTop, 1) is { } maskY)
            SetInputCursor(inputX, maskY, inputWidth, mask);
        else if (focusRow == 1 && InputCursorY(frameBounds, bodyScrollTop, 3) is { } textY)
            SetInputCursor(inputX, textY, inputWidth, text);
        else if (focusRow == 9 && InputCursorY(frameBounds, bodyScrollTop, 12) is { } parallelismY)
            SetInputCursor(inputX, parallelismY, Math.Min(8, inputWidth), parallelism);
        else
            _screen.SetCursorVisible(false);
    }

    private static int NormalizeBodyScroll(ConsoleSize size, int focusRow, int bodyScrollTop)
    {
        int viewportRows = SearchBodyViewportRows(size);
        bodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(bodyScrollTop, BodyRowCount, viewportRows);
        int focusVirtualRow = FocusVirtualRow(focusRow);
        if (focusVirtualRow >= 0)
            bodyScrollTop = ScrollStateCalculator.EnsureIndexVisible(focusVirtualRow, bodyScrollTop, viewportRows);
        return ScrollStateCalculator.ClampFirstVisibleIndex(bodyScrollTop, BodyRowCount, viewportRows);
    }

    private static int SearchBodyViewportRows(ConsoleSize size)
    {
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int frameHeight = Math.Max(1, dialogHeight - 2);
        int buttonRow = frameHeight - 2;
        int errorRow = buttonRow - 1;
        return Math.Max(1, errorRow - 1);
    }

    private static int FocusVirtualRow(int focusRow) => focusRow switch
    {
        0 => 1,
        1 => 3,
        3 => 5,
        4 => 6,
        5 => 7,
        6 => 8,
        7 => 9,
        8 => 10,
        9 => 12,
        _ => -1,
    };

    private static int? InputCursorY(Rect frameBounds, int bodyScrollTop, int virtualRow)
    {
        int buttonY = frameBounds.Y + frameBounds.Height - 2;
        int errorY = buttonY - 1;
        int bodyTop = frameBounds.Y + 1;
        int bodyHeight = Math.Max(1, errorY - bodyTop);
        int row = virtualRow - bodyScrollTop;
        return row >= 0 && row < bodyHeight ? bodyTop + row : null;
    }

    private void DrawInput(int x, int y, int width, CommandLineState buffer, bool focused)
    {
        string text = VisibleInputText(buffer, width);
        _screen.Write(x, y, text.PadRight(width), focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input);
    }

    private void DrawValueRow(
        int x,
        int y,
        int width,
        string label,
        string value,
        bool focused,
        CellStyle fill,
        CellStyle focusedStyle)
    {
        string labelText = $"{label} ";
        int labelWidth = Math.Min(labelText.Length, width);
        _screen.Write(x, y, labelText[..labelWidth], fill);

        int valueWidth = Math.Max(0, width - labelWidth);
        if (valueWidth == 0)
            return;

        string valueText = Truncate(value, valueWidth).PadRight(valueWidth);
        _screen.Write(x + labelWidth, y, valueText, focused ? focusedStyle : FarDialogStyles.Input);
    }

    private void DrawCheckbox(
        int x,
        int y,
        int width,
        string label,
        bool value,
        bool focused,
        CellStyle fill,
        CellStyle focusedStyle)
    {
        string text = $"[{(value ? "x" : " ")}] {label}";
        _screen.Write(x, y, Truncate(text, width).PadRight(width), focused ? focusedStyle : fill);
    }

    private void SetInputCursor(int x, int y, int width, CommandLineState buffer)
    {
        int start = Math.Max(0, buffer.CursorPosition - (width - 1));
        int cursorX = x + buffer.CursorPosition - start;
        if (cursorX < x + width)
        {
            _screen.SetCursorPosition(cursorX, y);
            _screen.SetCursorVisible(true);
        }
    }

    private static string VisibleInputText(CommandLineState buffer, int width)
    {
        int start = Math.Max(0, buffer.CursorPosition - (width - 1));
        string visible = buffer.Text.Length > start ? buffer.Text[start..] : string.Empty;
        return visible.Length > width ? visible[..width] : visible;
    }

    private static string ScopeLabel(SearchScope scope) => scope switch
    {
        SearchScope.CurrentDirectoryOnly => "In current folder",
        _ => "From the current folder",
    };

    private int HitTestOptionRow(MouseConsoleInputEvent mouse)
    {
        for (int row = 0; row < _optionBounds.Length; row++)
        {
            var b = _optionBounds[row];
            if (b.Width == 0) continue;
            if (mouse.X >= b.X && mouse.X < b.Right &&
                mouse.Y >= b.Y && mouse.Y < b.Bottom)
                return row;
        }
        return -1;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "~";
    }
}
