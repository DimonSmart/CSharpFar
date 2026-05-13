using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

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
        string? error = null;

        while (true)
        {
            Draw(title, prompt, buf, error, maskInput, size);

            var key = _screen.ReadKey();

            if (key.Key == ConsoleKey.Escape)
                return null;

            if (key.Key == ConsoleKey.Enter)
            {
                string text = buf.Text.Trim();
                if (text.Length == 0 && !allowEmpty) continue;

                error = validate?.Invoke(text);
                if (error is null) return text;
                continue;
            }

            // Editing — any typing clears the error
            bool isPrintable = key.KeyChar >= ' ' &&
                (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

            if (isPrintable) { buf.Insert(key.KeyChar); error = null; continue; }

            switch (key.Key)
            {
                case ConsoleKey.Backspace:  buf.DeleteBack();    error = null; break;
                case ConsoleKey.Delete:     buf.DeleteForward(); error = null; break;
                case ConsoleKey.LeftArrow:  buf.MoveCursor(-1);               break;
                case ConsoleKey.RightArrow: buf.MoveCursor(+1);               break;
                case ConsoleKey.Home:       buf.MoveToStart();                break;
                case ConsoleKey.End:        buf.MoveToEnd();                  break;
            }
        }
    }

    private void Draw(string title, string prompt, CommandLineState buf, string? error, bool maskInput, ConsoleSize size)
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
            DrawInputField(fieldX, fieldY, fieldWidth, buf, maskInput);

            string errorText = error is not null
                ? Truncate(error, fieldWidth).PadRight(fieldWidth)
                : new string(' ', fieldWidth);
            _screen.Write(fieldX, dlgY + 3, errorText, PaletteStyles.DialogError(_palette));

            int visStart = Math.Max(0, buf.CursorPosition - (fieldWidth - 1));
            int cursorScreenX = fieldX + (buf.CursorPosition - visStart);
            if (cursorScreenX < fieldX + fieldWidth)
            {
                _screen.SetCursorPosition(cursorScreenX, fieldY);
                _screen.SetCursorVisible(true);
            }
        });
    }

    private void DrawInputField(int x, int y, int width, CommandLineState buf, bool maskInput)
    {
        string text  = maskInput ? new string('*', buf.Text.Length) : buf.Text;
        int    cursor = buf.CursorPosition;
        int    start  = Math.Max(0, cursor - (width - 1));

        string visible = text.Length > start ? text[start..] : string.Empty;
        if (visible.Length > width) visible = visible[..width];

        _screen.Write(x, y, visible.PadRight(width), PaletteStyles.InputField(_palette));
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 1)] + "\u2026";
}
