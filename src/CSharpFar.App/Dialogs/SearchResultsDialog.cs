using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Shows a scrollable list of search results.
/// Enter → returns the selected path; Esc → returns null.
/// </summary>
internal sealed class SearchResultsDialog
{
    private const int DialogWidth = 70;
    private const int MaxVisible  = 15;

    private readonly ScreenRenderer _screen;

    public SearchResultsDialog(ScreenRenderer screen) => _screen = screen;

    public string? Show(IReadOnlyList<string> results)
    {
        if (results.Count == 0) return null;

        int visible = Math.Min(results.Count, MaxVisible);
        int dlgH    = visible + 2;

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            int cursor    = 0;
            int scrollTop = 0;

            while (true)
            {
                Draw(results, cursor, scrollTop, visible, dlgH, size);

                var key = _screen.ReadKey();
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        if (cursor > 0) { cursor--; if (cursor < scrollTop) scrollTop = cursor; }
                        break;

                    case ConsoleKey.DownArrow:
                        if (cursor < results.Count - 1)
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
                        cursor    = Math.Min(results.Count - 1, cursor + visible);
                        scrollTop = Math.Max(0, cursor - visible + 1);
                        break;

                    case ConsoleKey.Enter:  return results[cursor];
                    case ConsoleKey.Escape: return null;
                }
            }
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    private void Draw(
        IReadOnlyList<string> results, int cursor, int scrollTop,
        int visible, int dlgH, ConsoleSize size)
    {
        int dlgX = Math.Max(0, (size.Width  - DialogWidth) / 2);
        int dlgY = Math.Max(0, (size.Height - dlgH)        / 2);
        int fw   = DialogWidth - 4;

        var bounds = new Rect(dlgX, dlgY, DialogWidth, dlgH);
        _screen.FillRegion(bounds, Theme.DialogFill);
        _screen.DrawBox(bounds, Theme.DialogBorder);

        string title = $" Search Results ({results.Count}) ";
        _screen.Write(dlgX + (DialogWidth - title.Length) / 2, dlgY, title, Theme.DialogTitle);

        for (int i = 0; i < visible; i++)
        {
            int idx = scrollTop + i;
            if (idx >= results.Count) break;

            string text  = Truncate(results[idx], fw).PadRight(fw);
            var    style = idx == cursor ? Theme.InputField : Theme.DialogFill;
            _screen.Write(dlgX + 2, dlgY + 1 + i, text, style);
        }

        _screen.SetCursorVisible(false);
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
