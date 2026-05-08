using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Dialogs;

public enum SaveChangesChoice { Save, Discard, Cancel }

/// <summary>
/// Asks whether to save, discard, or cancel closing a modified file.
/// S/Enter → Save, D → Discard, C/Esc → Cancel.
/// </summary>
internal sealed class SaveChangesDialog
{
    private const int DialogWidth  = 52;
    private const int DialogHeight = 5;

    private readonly ScreenRenderer _screen;

    public SaveChangesDialog(ScreenRenderer screen) => _screen = screen;

    public SaveChangesChoice Show(string fileName)
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
                    case ConsoleKey.S:
                    case ConsoleKey.Enter:  return SaveChangesChoice.Save;
                    case ConsoleKey.D:      return SaveChangesChoice.Discard;
                    case ConsoleKey.C:
                    case ConsoleKey.Escape: return SaveChangesChoice.Cancel;
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
        new DialogFrameRenderer().RenderFrame(_screen, bounds, "Save Changes?", false, Theme.DialogPopupOptions, (_, _) =>
        {
            string msg = Truncate($"\"{fileName}\" has been modified.", fw).PadRight(fw);
            _screen.Write(dlgX + 2, dlgY + 1, msg, Theme.DialogFill);

            const string buttons = "[S]ave   [D]iscard   [C]ancel";
            _screen.Write(dlgX + (DialogWidth - buttons.Length) / 2, dlgY + 3, buttons, Theme.DialogFill);
        });
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
