using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>Shows a simple message box and waits for Enter or Esc.</summary>
internal sealed class MessageDialog
{
    private const int DialogWidth  = 52;
    private const int DialogHeight = 5;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public MessageDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public void Show(string title, string message)
    {
        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            Draw(title, message, size);
            _screen.SetCursorVisible(false);

            while (true)
            {
                var key = _screen.ReadKey();
                if (key.Key is ConsoleKey.Enter or ConsoleKey.Escape) break;
            }
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    private void Draw(string title, string message, ConsoleSize size)
    {
        int dlgX = Math.Max(0, (size.Width  - DialogWidth)  / 2);
        int dlgY = Math.Max(0, (size.Height - DialogHeight) / 2);
        int fw   = DialogWidth - 4;

        var bounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);
        new DialogFrameRenderer().RenderFrame(_screen, bounds, title, false, PaletteStyles.DialogPopupOptions(_palette), (_, _) =>
        {
            _screen.Write(dlgX + 2, dlgY + 1, Truncate(message, fw).PadRight(fw), PaletteStyles.DialogError(_palette));

            const string hint = "[ Press Enter ]";
            _screen.Write(dlgX + (DialogWidth - hint.Length) / 2, dlgY + 3, hint, PaletteStyles.DialogFill(_palette));
        });
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
