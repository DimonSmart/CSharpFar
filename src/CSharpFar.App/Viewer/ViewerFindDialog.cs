using System.Text.RegularExpressions;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed class ViewerFindDialog
{
    private const int DialogWidth = 60;
    private const int DialogHeight = 12;
    private const int ButtonFocusRow = 5;

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("find", "Find", 'F', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public ViewerFindDialog(ScreenRenderer screen, ConsolePalette palette)
    {
        _screen = screen;
        _palette = palette;
    }

    public ViewerFindDialogResult? Show(ViewerSearchRequest? previous, bool hexMode)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(size, previous, hexMode);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private ViewerFindDialogResult? RunLoop(
        ConsoleSize size,
        ViewerSearchRequest? previous,
        bool hexMode)
    {
        var pattern = new CommandLineState();
        if (previous is not null)
            pattern.SetText(previous.Pattern);

        bool caseSensitive = previous?.CaseSensitive ?? false;
        bool wholeWords = previous?.WholeWords ?? false;
        bool useRegex = previous?.UseRegex ?? false;
        bool searchHex = previous?.SearchHex ?? hexMode;
        if (searchHex)
            useRegex = false;

        SingleLineTextHistoryState patternHistory = HistoryRegistry.GetOrCreate("Viewer.Find.Pattern");
        int focusRow = 0;
        int focusedButton = 0;
        string? error = null;

        while (true)
        {
            Draw(size, pattern, patternHistory, caseSensitive, wholeWords, useRegex, searchHex, focusRow, focusedButton, error);
            var input = _screen.ReadInput();
            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

            if (focusRow == 0)
            {
                int availableRows = SingleLineTextInput.AvailableDropdownContentRows(InputFieldY(size), size.Height);
                if (patternHistory.IsDropdownOpen &&
                    key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape)
                {
                    SingleLineTextInput.HandleKey(pattern, key, ref error, patternHistory, availableRows);
                    continue;
                }

                if (key.Key is ConsoleKey.DownArrow or ConsoleKey.Tab)
                {
                    focusRow = 1;
                    continue;
                }

                if (SingleLineTextInput.HandleKey(pattern, key, ref error, patternHistory, availableRows) != TextInputKeyResult.Ignored)
                    continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return null;
                case ConsoleKey.UpArrow:
                    focusRow = Math.Max(0, focusRow - 1);
                    break;
                case ConsoleKey.DownArrow:
                case ConsoleKey.Tab:
                    focusRow = Math.Min(ButtonFocusRow, focusRow + 1);
                    break;
                case ConsoleKey.Spacebar:
                case ConsoleKey.Enter:
                    if (focusRow == 1)
                        caseSensitive = !caseSensitive;
                    else if (focusRow == 2)
                        wholeWords = !wholeWords;
                    else if (focusRow == 3)
                    {
                        useRegex = !useRegex;
                        if (useRegex)
                            searchHex = false;
                    }
                    else if (focusRow == 4)
                    {
                        searchHex = !searchHex;
                        if (searchHex)
                            useRegex = false;
                    }
                    else
                    {
                        var result = TryAccept(pattern.Text, caseSensitive, wholeWords, useRegex, searchHex, patternHistory, ref error);
                        if (result is not null)
                            return result;
                    }
                    break;
            }

            if (focusRow == ButtonFocusRow &&
                _buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                if (buttonId == "cancel")
                    return null;
                if (buttonId == "find")
                {
                    var result = TryAccept(pattern.Text, caseSensitive, wholeWords, useRegex, searchHex, patternHistory, ref error);
                    if (result is not null)
                        return result;
                }
            }
        }
    }

    private static ViewerFindDialogResult? TryAccept(
        string pattern,
        bool caseSensitive,
        bool wholeWords,
        bool useRegex,
        bool searchHex,
        SingleLineTextHistoryState patternHistory,
        ref string? error)
    {
        if (pattern.Length == 0)
        {
            error = "Search text is required.";
            return null;
        }

        if (searchHex && !ViewerSearchEngine.TryParseHexPattern(pattern, out _, out error))
            return null;

        if (useRegex)
        {
            try
            {
                _ = new Regex(pattern);
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        patternHistory.Add(pattern);
        error = null;
        return new ViewerFindDialogResult(pattern, caseSensitive, wholeWords, useRegex, searchHex);
    }

    private void Draw(
        ConsoleSize size,
        CommandLineState pattern,
        SingleLineTextHistoryState patternHistory,
        bool caseSensitive,
        bool wholeWords,
        bool useRegex,
        bool searchHex,
        int focusRow,
        int focusedButton,
        string? error)
    {
        using var frame = _screen.BeginFrame();

        Rect outerBounds = _modalRenderer.CenteredOuterBounds(_screen, DialogWidth, DialogHeight, minHeight: DialogHeight);
        _modalRenderer.Render(
            _screen,
            outerBounds,
            "Find",
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false },
            PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false },
            (_, layout) =>
            {
                Rect content = layout.ContentBounds;
                _screen.Write(content.X, content.Y, "Text", PaletteStyles.DialogFill(_palette));
                SingleLineTextInput.Render(
                    _screen,
                    content.X + 10,
                    content.Y,
                    Math.Max(1, content.Width - 10),
                    pattern,
                    focusRow == 0 ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette),
                    PaletteStyles.InputHighlight(_palette),
                    patternHistory,
                    _palette);

                DrawCheckbox(content.X, content.Y + 2, content.Width, "Case sensitive", caseSensitive, focusRow == 1);
                DrawCheckbox(content.X, content.Y + 3, content.Width, "Whole words", wholeWords, focusRow == 2);
                DrawCheckbox(content.X, content.Y + 4, content.Width, "Regular expression", useRegex, focusRow == 3);
                DrawCheckbox(content.X, content.Y + 5, content.Width, "Hex sequence", searchHex, focusRow == 4);

                string errorText = error is null ? string.Empty : error;
                _screen.Write(content.X, content.Y + 6, Fit(errorText, content.Width), PaletteStyles.DialogError(_palette));
                _buttonBar.Render(
                    _screen,
                    content.X,
                    content.Y + 7,
                    content.Width,
                    focusRow == ButtonFocusRow ? focusedButton : -1,
                    PaletteStyles.DialogFill(_palette),
                    PaletteStyles.InputField(_palette));

                if (focusRow == 0)
                {
                    int cursorX = Math.Min(content.X + 10 + pattern.CursorPosition, content.Right - 1);
                    _screen.SetCursorPosition(cursorX, content.Y);
                    _screen.SetCursorVisible(true);
                }
                else
                {
                    _screen.SetCursorVisible(false);
                }
            });

        _ = size;
    }

    private static int InputFieldY(ConsoleSize size) =>
        Math.Max(0, (size.Height - DialogHeight) / 2) + 2;

    private void DrawCheckbox(int x, int y, int width, string label, bool value, bool focused)
    {
        string text = $"[{(value ? 'x' : ' ')}] {label}";
        _screen.Write(x, y, Fit(text, width), focused ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette));
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
