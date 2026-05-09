using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Viewer;

/// <summary>
/// Full-screen text file viewer.
/// Keys: Up/Down/PgUp/PgDn/Home/End scroll vertically,
///       Left/Right scroll horizontally, F10/Esc exit.
/// </summary>
internal sealed class FileViewer
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public FileViewer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public void Show(string filePath)
    {
        if (!File.Exists(filePath))
        {
            new MessageDialog(_screen, _palette).Show("Viewer", "File not found.");
            return;
        }

        var info = new FileInfo(filePath);
        if (info.Length > TextFileReader.MaxFileSizeBytes)
        {
            new MessageDialog(_screen, _palette).Show(
                "Viewer",
                $"File too large (max {TextFileReader.MaxFileSizeBytes / 1024 / 1024} MB).");
            return;
        }

        string[] lines;
        try   { lines = TextFileReader.ReadLines(filePath); }
        catch (Exception ex)
        {
            new MessageDialog(_screen, _palette).Show("Viewer", ex.Message);
            return;
        }

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        try   { RunLoop(filePath, lines); }
        finally { _screen.Restore(saved); }
    }

    // ── main loop ─────────────────────────────────────────────────────────────

    private void RunLoop(string filePath, string[] lines)
    {
        int scrollTop  = 0;
        int scrollLeft = 0;

        while (true)
        {
            var size     = _screen.GetSize();
            int contentH = size.Height - 2; // rows reserved for header + footer

            Draw(filePath, lines, scrollTop, scrollLeft, contentH, size);

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

                case ConsoleKey.Escape:
                case ConsoleKey.F10:
                    return;
            }
        }
    }

    // ── rendering ─────────────────────────────────────────────────────────────

    private void Draw(
        string filePath, string[] lines,
        int scrollTop, int scrollLeft, int contentH, ConsoleSize size)
    {
        _screen.SetCursorVisible(false);

        // Header: filename on the left, line/total on the right
        string nameSection = $" {Path.GetFileName(filePath)} ";
        string posSection  = lines.Length == 0
            ? " 0/0 "
            : $" {scrollTop + 1}/{lines.Length} ";
        int nameWidth = Math.Max(0, size.Width - posSection.Length);
        if (nameSection.Length > nameWidth)
            nameSection = nameSection[..nameWidth];
        string header = nameSection.PadRight(nameWidth) + posSection;
        _screen.Write(0, 0, header, PaletteStyles.PathHeaderActive(_palette));

        // Content
        for (int i = 0; i < contentH; i++)
        {
            int lineIdx = scrollTop + i;
            string text = lineIdx < lines.Length
                ? FormatLine(lines[lineIdx], scrollLeft, size.Width)
                : new string(' ', size.Width);
            _screen.Write(0, i + 1, text, PaletteStyles.CommandLine(_palette));
        }

        // Footer key bar
        _screen.FillRegion(new Rect(0, size.Height - 1, size.Width, 1), PaletteStyles.KeyBarLabel(_palette));
        _screen.Write(0, size.Height - 1, "10", PaletteStyles.KeyBarNum(_palette));
        _screen.Write(2, size.Height - 1, "Close", PaletteStyles.KeyBarLabel(_palette));
    }

    private static string FormatLine(string line, int scrollLeft, int width)
    {
        line = line.Replace("\t", "    "); // expand tabs to 4 spaces

        if (scrollLeft >= line.Length) return new string(' ', width);

        string visible = line[scrollLeft..];
        return visible.Length <= width ? visible.PadRight(width) : visible[..width];
    }
}
