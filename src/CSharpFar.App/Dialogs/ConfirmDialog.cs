using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>Asks the user to confirm a destructive action. Returns true if confirmed.</summary>
internal sealed class ConfirmDialog
{
    private const int DialogWidth  = 52;
    private const int DialogHeight = 5;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public ConfirmDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    /// <summary>
    /// Draws the dialog and waits for input.
    /// Enter or D → true (confirmed). Esc or C → false (cancelled).
    /// </summary>
    public bool Show(string title, string prompt)
    {
        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            Draw(title, prompt, size);
            _screen.SetCursorVisible(false);

            while (true)
            {
                var key = _screen.ReadKey();
                switch (key.Key)
                {
                    case ConsoleKey.D:
                    case ConsoleKey.Enter:  return true;
                    case ConsoleKey.C:
                    case ConsoleKey.Escape: return false;
                }
            }
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    private void Draw(string title, string prompt, ConsoleSize size)
    {
        int dlgX = Math.Max(0, (size.Width  - DialogWidth)  / 2);
        int dlgY = Math.Max(0, (size.Height - DialogHeight) / 2);
        int fw   = DialogWidth - 4;

        var bounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);
        new DialogFrameRenderer().RenderFrame(_screen, bounds, title, false, PaletteStyles.DialogPopupOptions(_palette), (_, _) =>
        {
            _screen.Write(dlgX + 2, dlgY + 1, Truncate(prompt, fw).PadRight(fw), PaletteStyles.DialogFill(_palette));

            const string buttons = "[D]elete   [C]ancel";
            _screen.Write(dlgX + (DialogWidth - buttons.Length) / 2, dlgY + 3, buttons, PaletteStyles.DialogFill(_palette));
        });
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
