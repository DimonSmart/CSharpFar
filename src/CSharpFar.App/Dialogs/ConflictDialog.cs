using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>Shows a "file already exists" dialog and returns the user's choice.</summary>
internal sealed class ConflictDialog
{
    private const int DialogWidth  = 46;
    private const int DialogHeight = 5;

    private readonly ScreenRenderer _screen;

    public ConflictDialog(ScreenRenderer screen) => _screen = screen;

    public ConflictChoice Show(string destPath)
    {
        var size = _screen.GetSize();
        int dlgX = Math.Max(0, (size.Width  - DialogWidth)  / 2);
        int dlgY = Math.Max(0, (size.Height - DialogHeight) / 2);
        int fw   = DialogWidth - 4;

        var bounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);
        _screen.FillRegion(bounds, Theme.DialogFill);
        _screen.DrawBox(bounds, Theme.DialogBorder);

        const string title = " File Already Exists ";
        _screen.Write(dlgX + (DialogWidth - title.Length) / 2, dlgY, title, Theme.DialogTitle);

        string name = Path.GetFileName(destPath);
        _screen.Write(dlgX + 2, dlgY + 1, Truncate(name, fw).PadRight(fw), Theme.DialogFill);

        const string buttons = "[O]verwrite   [S]kip   [C]ancel";
        _screen.Write(dlgX + (DialogWidth - buttons.Length) / 2, dlgY + 3, buttons, Theme.DialogFill);

        _screen.SetCursorVisible(false);

        while (true)
        {
            var key = _screen.ReadKey();
            switch (key.Key)
            {
                case ConsoleKey.O: return ConflictChoice.Overwrite;
                case ConsoleKey.S: return ConflictChoice.Skip;
                case ConsoleKey.C:
                case ConsoleKey.Escape: return ConflictChoice.Cancel;
            }
        }
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
