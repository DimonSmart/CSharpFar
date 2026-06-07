using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal sealed class EditorFindDialog
{
    private const int DialogWidth = 56;
    private const int DialogHeight = 10;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("find", "Find", 'F', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public EditorFindDialog(ScreenRenderer screen, ConsolePalette palette)
    {
        _screen = screen;
        _palette = palette;
    }

    public EditorFindDialogResult? Show(EditorFindDialogResult? previous)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(size, previous);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private EditorFindDialogResult? RunLoop(ConsoleSize size, EditorFindDialogResult? previous)
    {
        var pattern = new CommandLineState();
        if (previous is not null)
            pattern.SetText(previous.Pattern);

        bool caseSensitive = previous?.CaseSensitive ?? false;
        bool wholeWords = previous?.WholeWords ?? false;
        SingleLineTextHistoryState patternHistory = HistoryRegistry.GetOrCreate("Editor.Find.Pattern");
        int focusRow = 0;
        int focusedButton = 0;
        string? error = null;

        while (true)
        {
            Draw(size, pattern, patternHistory, caseSensitive, wholeWords, focusRow, focusedButton, error);
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
                    focusRow = Math.Min(3, focusRow + 1);
                    break;
                case ConsoleKey.Spacebar:
                    if (focusRow == 1)
                        caseSensitive = !caseSensitive;
                    else if (focusRow == 2)
                        wholeWords = !wholeWords;
                    break;
                case ConsoleKey.Enter:
                    if (focusRow == 1)
                    {
                        caseSensitive = !caseSensitive;
                        break;
                    }
                    if (focusRow == 2)
                    {
                        wholeWords = !wholeWords;
                        break;
                    }

                    if (pattern.Text.Length == 0)
                    {
                        error = "Search text is required.";
                        break;
                    }

                    patternHistory.Add(pattern.Text);
                    return new EditorFindDialogResult(pattern.Text, caseSensitive, wholeWords);
            }

            if (focusRow == 3 &&
                _buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                if (buttonId == "cancel")
                    return null;
                if (buttonId == "find")
                {
                    if (pattern.Text.Length == 0)
                    {
                        error = "Search text is required.";
                        continue;
                    }

                    patternHistory.Add(pattern.Text);
                    return new EditorFindDialogResult(pattern.Text, caseSensitive, wholeWords);
                }
            }
        }
    }

    private void Draw(
        ConsoleSize size,
        CommandLineState pattern,
        SingleLineTextHistoryState patternHistory,
        bool caseSensitive,
        bool wholeWords,
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

                DrawCheckbox(content.X, content.Y + 2, content.Width, "CaseSensitive", caseSensitive, focusRow == 1);
                DrawCheckbox(content.X, content.Y + 3, content.Width, "WholeWords", wholeWords, focusRow == 2);

                string errorText = error is null ? string.Empty : error;
                _screen.Write(content.X, content.Y + 4, Fit(errorText, content.Width), PaletteStyles.DialogError(_palette));
                _buttonBar.Render(
                    _screen,
                    content.X,
                    content.Y + 5,
                    content.Width,
                    focusRow == 3 ? focusedButton : -1,
                    PaletteStyles.DialogFill(_palette),
                    PaletteStyles.InputField(_palette));

                if (focusRow == 0)
                {
                    int inputX = content.X + 10;
                    int inputWidth = Math.Max(1, content.Width - 10);
                    int textWidth = Math.Max(0, inputWidth - 1);
                    int cursorX = SingleLineTextInput.GetCursorX(inputX, textWidth, pattern);
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
