using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class FarNetInputDialog
{
    private const int DialogWidth = 52;
    private const int DialogHeight = 6;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public FarNetInputDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public string? Show(string title, string prompt, string? initialText)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            var buffer = new CommandLineState();
            if (initialText is not null)
                buffer.SetText(initialText);

            string? error = null;
            while (true)
            {
                Draw(title, prompt, buffer, size);
                var input = _screen.ReadInput();
                if (input is not KeyConsoleInputEvent { Key: var key })
                    continue;

                if (key.Key == ConsoleKey.Escape)
                    return null;

                if (key.Key == ConsoleKey.Enter)
                    return buffer.Text;

                SingleLineTextInput.HandleKey(buffer, key, ref error);
            }
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private void Draw(string title, string prompt, CommandLineState buffer, ConsoleSize size)
    {
        int dlgX = Math.Max(0, (size.Width - DialogWidth) / 2);
        int dlgY = Math.Max(0, (size.Height - DialogHeight) / 2);
        int fieldWidth = DialogWidth - 4;
        var bounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);

        new DialogFrameRenderer().RenderFrame(_screen, bounds, title, false, PaletteStyles.DialogPopupOptions(_palette), (_, _) =>
        {
            _screen.Write(dlgX + 2, dlgY + 1, Truncate(prompt, fieldWidth).PadRight(fieldWidth), PaletteStyles.DialogFill(_palette));
            SingleLineTextInput.Render(
                _screen,
                dlgX + 2,
                dlgY + 2,
                fieldWidth,
                buffer,
                PaletteStyles.InputField(_palette),
                PaletteStyles.InputHighlight(_palette));

            int cursorX = SingleLineTextInput.GetCursorX(dlgX + 2, fieldWidth, buffer);
            _screen.SetCursorPosition(Math.Min(dlgX + 1 + fieldWidth, cursorX), dlgY + 2);
            _screen.SetCursorVisible(true);
        });
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..Math.Max(0, maxLen - 1)] + "\u2026";
}
