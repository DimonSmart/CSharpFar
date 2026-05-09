using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Dialogs;

public enum OpenFileChoice { View, Edit, Cancel }

/// <summary>
/// Asks whether to open a file in the viewer or the editor.
/// V → View, E → Edit, Esc/C → Cancel.
/// </summary>
internal sealed class OpenFileDialog
{
    private const int DialogWidth  = 52;
    private const int DialogHeight = 5;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public OpenFileDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public OpenFileChoice Show(string fileName)
    {
        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            Draw(fileName, size);
            _screen.SetCursorVisible(false);

            while (true)
            {
                var key = _screen.ReadKey();
                switch (key.Key)
                {
                    case ConsoleKey.V:      return OpenFileChoice.View;
                    case ConsoleKey.E:      return OpenFileChoice.Edit;
                    case ConsoleKey.C:
                    case ConsoleKey.Escape: return OpenFileChoice.Cancel;
                }
            }
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    private void Draw(string fileName, ConsoleSize size)
    {
        int dlgX = Math.Max(0, (size.Width  - DialogWidth)  / 2);
        int dlgY = Math.Max(0, (size.Height - DialogHeight) / 2);
        int fw   = DialogWidth - 4;

        var bounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);
        new DialogFrameRenderer().RenderFrame(_screen, bounds, "Open File", false, PaletteStyles.DialogPopupOptions(_palette), (_, _) =>
        {
            string msg = Truncate($"Open \"{fileName}\" as:", fw).PadRight(fw);
            _screen.Write(dlgX + 2, dlgY + 1, msg, PaletteStyles.DialogFill(_palette));

            const string buttons = "[V]iew   [E]dit   [C]ancel";
            _screen.Write(dlgX + (DialogWidth - buttons.Length) / 2, dlgY + 3, buttons, PaletteStyles.DialogFill(_palette));
        });
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
