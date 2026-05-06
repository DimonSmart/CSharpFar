using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Viewer;

/// <summary>
/// Full-screen built-in help viewer.
/// Keys: Up/Down/PgUp/PgDn/Home/End scroll; Left/Right scroll horizontally;
/// F1/F10/Esc exit.
/// </summary>
internal sealed class HelpViewer
{
    private readonly ScreenRenderer _screen;

    public HelpViewer(ScreenRenderer screen) => _screen = screen;

    public void Show()
    {
        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        try   { RunLoop(); }
        finally { _screen.Restore(saved); }
    }

    // ── main loop ─────────────────────────────────────────────────────────────

    private void RunLoop()
    {
        var    lines      = HelpContent.Lines;
        int    scrollTop  = 0;
        int    scrollLeft = 0;

        while (true)
        {
            var size     = _screen.GetSize();
            int contentH = size.Height - 2;

            Draw(lines, scrollTop, scrollLeft, contentH, size);

            var key = _screen.ReadKey();
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (scrollTop > 0) scrollTop--;
                    break;

                case ConsoleKey.DownArrow:
                    scrollTop = Math.Min(Math.Max(0, lines.Length - contentH), scrollTop + 1);
                    break;

                case ConsoleKey.LeftArrow:
                    if (scrollLeft > 0) scrollLeft--;
                    break;

                case ConsoleKey.RightArrow:
                    scrollLeft++;
                    break;

                case ConsoleKey.PageUp:
                    scrollTop = Math.Max(0, scrollTop - contentH);
                    break;

                case ConsoleKey.PageDown:
                    scrollTop = Math.Min(Math.Max(0, lines.Length - contentH), scrollTop + contentH);
                    break;

                case ConsoleKey.Home:
                    scrollTop  = 0;
                    scrollLeft = 0;
                    break;

                case ConsoleKey.End:
                    scrollTop  = Math.Max(0, lines.Length - contentH);
                    scrollLeft = 0;
                    break;

                case ConsoleKey.F1:
                case ConsoleKey.F10:
                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    // ── rendering ─────────────────────────────────────────────────────────────

    private void Draw(
        string[] lines, int scrollTop, int scrollLeft,
        int contentH, ConsoleSize size)
    {
        _screen.SetCursorVisible(false);

        // Header
        const string title    = " CSharpFar Help ";
        string       posStr   = lines.Length == 0 ? " 0/0 " : $" {scrollTop + 1}/{lines.Length} ";
        int          nameWidth = Math.Max(0, size.Width - posStr.Length);
        string       header    = title.PadRight(nameWidth)[..nameWidth] + posStr;
        _screen.Write(0, 0, header, Theme.PathHeaderActive);

        // Content
        for (int i = 0; i < contentH; i++)
        {
            int    lineIdx = scrollTop + i;
            string text    = lineIdx < lines.Length
                ? FormatLine(lines[lineIdx], scrollLeft, size.Width)
                : new string(' ', size.Width);
            _screen.Write(0, i + 1, text, Theme.CommandLine);
        }

        // Footer key bar
        _screen.FillRegion(new Rect(0, size.Height - 1, size.Width, 1), Theme.KeyBarLabel);
        _screen.Write(0, size.Height - 1, "10", Theme.KeyBarNum);
        _screen.Write(2, size.Height - 1, "Close", Theme.KeyBarLabel);
    }

    private static string FormatLine(string line, int scrollLeft, int width)
    {
        if (scrollLeft >= line.Length) return new string(' ', width);
        string visible = line[scrollLeft..];
        return visible.Length <= width ? visible.PadRight(width) : visible[..width];
    }
}
