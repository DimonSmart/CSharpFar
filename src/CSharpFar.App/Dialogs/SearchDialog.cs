using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

internal sealed class SearchDialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 18;
    private const int ButtonRow = 10;

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();

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
        mask.SetText("*");

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
        int focusedButton = 0;
        string? error = null;
        var buttonBar = new DialogButtonBar(
        [
            new DialogButton("find", "Find", 'F', IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C'),
        ]);

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

            if (input is MouseConsoleInputEvent &&
                buttonBar.TryHandleInput(input, ref focusedButton, out buttonId) &&
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

        void DrawCurrent()
        {
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
                buttonBar,
                focusedButton,
                error);
        }
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
        DialogButtonBar buttonBar,
        int focusedButton,
        string? error)
    {
        using var frame = _screen.BeginFrame();

        int dialogWidth = Math.Min(DialogWidth, Math.Max(48, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(16, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        var outerBounds = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);

        var fill = FarDialogStyles.Fill;
        var focused = FarDialogStyles.Input;
        var disabled = new CellStyle(ConsoleColor.DarkGray, fill.Background);

        _modalRenderer.Render(_screen, outerBounds, "Find file", true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);

            _screen.Write(contentX, bounds.Y + 1, "A file mask or several file masks:".PadRight(contentWidth), fill);
            DrawInput(contentX, bounds.Y + 2, contentWidth, mask, focusRow == 0);

            _screen.Write(contentX, bounds.Y + 3, "Containing text:".PadRight(contentWidth), fill);
            DrawInput(contentX, bounds.Y + 4, contentWidth, text, focusRow == 1);

            DrawValueRow(contentX, bounds.Y + 5, contentWidth, "Using code page:", "Automatic detection", false, fill, focused);
            DrawCheckbox(contentX, bounds.Y + 6, contentWidth, "Case sensitive", caseSensitive, focusRow == 3, fill, focused);
            DrawCheckbox(contentX, bounds.Y + 7, contentWidth, "Whole words", wholeWords, focusRow == 4, fill, focused);
            DrawCheckbox(
                contentX,
                bounds.Y + 8,
                contentWidth,
                "Not containing",
                text.Text.Length > 0 && notContaining,
                focusRow == 5,
                text.Text.Length > 0 ? fill : disabled,
                focused);
            DrawCheckbox(contentX, bounds.Y + 9, contentWidth, "Search folders", includeDirectoriesInResults, focusRow == 6, fill, focused);
            DrawCheckbox(contentX, bounds.Y + 10, contentWidth, "Search in symbolic links", searchInSymbolicLinks, focusRow == 7, fill, focused);
            DrawValueRow(contentX, bounds.Y + 11, contentWidth, "Select search area:", ScopeLabel(scope), focusRow == 8, fill, focused);

            _screen.Write(contentX, bounds.Y + 12, "Parallelism:".PadRight(contentWidth), fill);
            DrawInput(contentX, bounds.Y + 13, Math.Min(8, contentWidth), parallelism, focusRow == 9);

            string errorText = error is null ? string.Empty : Truncate(error, contentWidth);
            _screen.Write(contentX, bounds.Y + 14, errorText.PadRight(contentWidth), FarDialogStyles.Error);

            buttonBar.Render(
                _screen,
                contentX,
                bounds.Y + bounds.Height - 2,
                contentWidth,
                focusedButton,
                fill,
                focusRow == ButtonRow ? focused : fill);
        });

        if (focusRow == 0)
            SetInputCursor(outerBounds.X + 3, outerBounds.Y + 3, Math.Max(1, outerBounds.Width - 6), mask);
        else if (focusRow == 1)
            SetInputCursor(outerBounds.X + 3, outerBounds.Y + 5, Math.Max(1, outerBounds.Width - 6), text);
        else if (focusRow == 9)
            SetInputCursor(outerBounds.X + 3, outerBounds.Y + 14, Math.Min(8, Math.Max(1, outerBounds.Width - 6)), parallelism);
        else
            _screen.SetCursorVisible(false);
    }

    private void DrawInput(int x, int y, int width, CommandLineState buffer, bool focused)
    {
        string text = VisibleInputText(buffer, width);
        _screen.Write(x, y, text.PadRight(width), FarDialogStyles.Input);
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

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "~";
    }
}
