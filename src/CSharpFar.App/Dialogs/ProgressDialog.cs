using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>Draws a non-modal progress overlay during copy operations.</summary>
internal sealed class ProgressDialog
{
    private const int DialogWidth  = 52;
    private const int DialogHeight = 4;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly string _destination;

    public ProgressDialog(ScreenRenderer screen, string destination, ConsolePalette? palette = null)
    {
        _screen      = screen;
        _destination = destination;
        _palette = palette ?? PaletteRegistry.Default;
    }

    /// <summary>Redraws the progress box showing <paramref name="currentFile"/>.</summary>
    public void Update(string currentFile)
    {
        var size = _screen.GetSize();
        int dlgX = Math.Max(0, (size.Width  - DialogWidth)  / 2);
        int dlgY = Math.Max(0, (size.Height - DialogHeight) / 2);
        int fw   = DialogWidth - 4;

        var bounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);
        new DialogFrameRenderer().RenderFrame(_screen, bounds, "Copying", false, PaletteStyles.DialogPopupOptions(_palette), (_, _) =>
        {
            string destLine = Truncate("To: " + _destination, fw);
            _screen.Write(dlgX + 2, dlgY + 1, destLine.PadRight(fw), PaletteStyles.DialogFill(_palette));
            _screen.Write(dlgX + 2, dlgY + 2, Truncate(currentFile, fw).PadRight(fw), PaletteStyles.DialogFill(_palette));
        });
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
