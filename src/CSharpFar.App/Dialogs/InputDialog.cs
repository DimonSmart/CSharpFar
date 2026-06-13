using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Modal single-line input dialog.
/// Draws a centered box, collects a string, validates on Enter, cancels on Esc.
/// </summary>
internal sealed class InputDialog
{
    private const int DialogWidth  = 44;
    private const int DialogHeight = 6;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();

    public InputDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    /// <summary>
    /// Shows the dialog and returns the entered text, or <c>null</c> if the user pressed Esc.
    /// </summary>
    /// <param name="title">Title shown in the top border.</param>
    /// <param name="prompt">Label shown above the input field.</param>
    /// <param name="validate">
    /// Called with the trimmed input on Enter.
    /// Return <c>null</c> to accept, or an error string to display and re-prompt.
    /// </param>
    public string? Show(
        string title,
        string prompt,
        string? initialText = null,
        Func<string, string?>? validate = null,
        bool allowEmpty = false,
        bool maskInput = false)
    {
        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(title, prompt, initialText, validate, allowEmpty, maskInput, size);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private string? RunLoop(string title, string prompt, string? initialText, Func<string, string?>? validate, bool allowEmpty, bool maskInput, ConsoleSize size)
    {
        var buf = new CommandLineState();
        if (initialText is not null) buf.SetText(initialText);
        SingleLineTextHistoryState? history = maskInput
            ? null
            : HistoryRegistry.GetOrCreate($"{title}\n{prompt}");
        ScrollBarDragState? historyScrollbarDrag = null;
        string? error = null;

        while (true)
        {
            Draw(title, prompt, buf, error, maskInput, size, history);

            int availableRows = SingleLineTextInput.AvailableDropdownContentRows(InputFieldY(size), size.Height);
            var input = _screen.ReadInput();
            if (input is MouseConsoleInputEvent mouse && history is not null)
            {
                int fieldX = InputFieldX(size);
                int fieldY = InputFieldY(size);
                int fieldWidth = InputFieldWidth(size);
                if (SingleLineTextInput.TryHandleHistoryDropdownMouse(
                        history,
                        buf,
                        mouse,
                        fieldX,
                        fieldY,
                        fieldWidth,
                        size.Height,
                        ref historyScrollbarDrag) ||
                    (SingleLineTextInput.IsHistoryArrowHit(fieldX, fieldWidth, fieldY, mouse.X, mouse.Y) &&
                     SingleLineTextInput.TryOpenHistoryDropdown(history, fieldY, size.Height)))
                {
                    continue;
                }
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

            if (history?.IsDropdownOpen == true &&
                key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape)
            {
                SingleLineTextInput.HandleKey(buf, key, ref error, history, availableRows);
                continue;
            }

            if (key.Key == ConsoleKey.Escape)
                return null;

            if (key.Key == ConsoleKey.Enter)
            {
                string text = buf.Text.Trim();
                if (text.Length == 0 && !allowEmpty) continue;

                error = validate?.Invoke(text);
                if (error is null)
                {
                    history?.Add(text);
                    return text;
                }
                continue;
            }

            if (SingleLineTextInput.HandleKey(buf, key, ref error, history, availableRows) != TextInputKeyResult.Ignored)
                continue;
        }
    }

    private void Draw(
        string title,
        string prompt,
        CommandLineState buf,
        string? error,
        bool maskInput,
        ConsoleSize size,
        SingleLineTextHistoryState? history)
    {
        int dlgX = Math.Max(0, (size.Width  - DialogWidth)  / 2);
        int dlgY = Math.Max(0, (size.Height - DialogHeight) / 2);
        var bounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);

        new DialogFrameRenderer().RenderFrame(_screen, bounds, title, false, PaletteStyles.DialogPopupOptions(_palette), (_, _) =>
        {
            _screen.Write(dlgX + 2, dlgY + 1, prompt, PaletteStyles.DialogFill(_palette));

            int fieldX     = dlgX + 2;
            int fieldY     = dlgY + 2;
            int fieldWidth = DialogWidth - 4;
            DrawInputField(fieldX, fieldY, fieldWidth, buf, maskInput, history);

            string errorText = error is not null
                ? Truncate(error, fieldWidth).PadRight(fieldWidth)
                : new string(' ', fieldWidth);
            _screen.Write(fieldX, dlgY + 3, errorText, PaletteStyles.DialogError(_palette));

            int textWidth = history is null ? fieldWidth : Math.Max(1, fieldWidth - 1);
            int visStart = Math.Max(0, buf.CursorPosition - (textWidth - 1));
            int cursorScreenX = fieldX + (buf.CursorPosition - visStart);
            if (cursorScreenX < fieldX + textWidth)
            {
                _screen.SetCursorPosition(cursorScreenX, fieldY);
                _screen.SetCursorVisible(true);
            }
        });
    }

    private void DrawInputField(
        int x,
        int y,
        int width,
        CommandLineState buf,
        bool maskInput,
        SingleLineTextHistoryState? history)
    {
        SingleLineTextInput.Render(
            _screen,
            x,
            y,
            width,
            buf,
            PaletteStyles.InputField(_palette),
            PaletteStyles.InputHighlight(_palette),
            history,
            maskInput: maskInput);
    }

    private static int InputFieldY(ConsoleSize size) =>
        Math.Max(0, (size.Height - DialogHeight) / 2) + 2;

    private static int InputFieldX(ConsoleSize size) =>
        Math.Max(0, (size.Width - DialogWidth) / 2) + 2;

    private static int InputFieldWidth(ConsoleSize size) => DialogWidth - 4;

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 1)] + "\u2026";
}
