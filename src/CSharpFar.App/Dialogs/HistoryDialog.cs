using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Shows the command history list (most recent first).
/// Returns the selected command text, or null if the user pressed Escape.
/// </summary>
internal sealed class HistoryDialog
{
    private const int DialogWidth = 60;
    private const int MaxVisible  = 15;

    private readonly ScreenRenderer _screen;

    public HistoryDialog(ScreenRenderer screen) => _screen = screen;

    public string? Show(IReadOnlyList<CommandHistoryItem> history)
    {
        if (history.Count == 0) return null;

        var cmds    = history.Select(i => i.Command).Reverse().ToList();
        int visible = Math.Min(cmds.Count, MaxVisible);
        int dlgH    = visible + 2; // top + bottom border

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            int cursor    = 0;
            int scrollTop = 0;

            while (true)
            {
                Draw(cmds, cursor, scrollTop, visible, dlgH, size);

                var key = _screen.ReadKey();
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        if (cursor > 0)
                        {
                            cursor--;
                            if (cursor < scrollTop) scrollTop = cursor;
                        }
                        break;

                    case ConsoleKey.DownArrow:
                        if (cursor < cmds.Count - 1)
                        {
                            cursor++;
                            if (cursor >= scrollTop + visible) scrollTop = cursor - visible + 1;
                        }
                        break;

                    case ConsoleKey.PageUp:
                        cursor    = Math.Max(0, cursor - visible);
                        scrollTop = Math.Max(0, scrollTop - visible);
                        break;

                    case ConsoleKey.PageDown:
                        cursor    = Math.Min(cmds.Count - 1, cursor + visible);
                        scrollTop = Math.Max(0, cursor - visible + 1);
                        break;

                    case ConsoleKey.Enter:
                        return cmds[cursor];

                    case ConsoleKey.Escape:
                        return null;
                }
            }
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    private void Draw(
        List<string> cmds, int cursor, int scrollTop,
        int visible, int dlgH, ConsoleSize size)
    {
        int dlgX = Math.Max(0, (size.Width  - DialogWidth) / 2);
        int dlgY = Math.Max(0, (size.Height - dlgH)        / 2);
        int fw   = DialogWidth - 4;

        var bounds = new Rect(dlgX, dlgY, DialogWidth, dlgH);
        _screen.FillRegion(bounds, Theme.DialogFill);
        _screen.DrawBox(bounds, Theme.DialogBorder);

        const string title = " Command History ";
        _screen.Write(dlgX + (DialogWidth - title.Length) / 2, dlgY, title, Theme.DialogTitle);

        for (int i = 0; i < visible; i++)
        {
            int idx = scrollTop + i;
            if (idx >= cmds.Count) break;

            string text  = Truncate(cmds[idx], fw).PadRight(fw);
            var    style = idx == cursor ? Theme.InputField : Theme.DialogFill;
            _screen.Write(dlgX + 2, dlgY + 1 + i, text, style);
        }

        _screen.SetCursorVisible(false);
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
