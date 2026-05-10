using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Viewer;

/// <summary>
/// Full-screen built-in help viewer with Far Manager-style syntax highlighting.
/// Keys: Up/Down/PgUp/PgDn/Home/End scroll; Left/Right scroll horizontally;
/// F1/F10/Esc exit.
/// </summary>
internal sealed class HelpViewer
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    // Column at which description starts for key-binding lines.
    private const int KeyColumnWidth = 20;

    public HelpViewer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

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
        HelpLine[] lines, int scrollTop, int scrollLeft,
        int contentH, ConsoleSize size)
    {
        _screen.SetCursorVisible(false);

        // Header
        const string title    = " CSharpFar Help ";
        string       posStr   = lines.Length == 0 ? " 0/0 " : $" {scrollTop + 1}/{lines.Length} ";
        int          nameWidth = Math.Max(0, size.Width - posStr.Length);
        string       header    = title.PadRight(nameWidth)[..nameWidth] + posStr;
        _screen.Write(0, 0, header, PaletteStyles.PathHeaderActive(_palette));

        // Content
        var bodyStyle = PaletteStyles.HelpBody(_palette);
        for (int i = 0; i < contentH; i++)
        {
            int lineIdx = scrollTop + i;
            int row     = i + 1;

            if (lineIdx >= lines.Length)
            {
                _screen.FillRegion(new Rect(0, row, size.Width, 1), bodyStyle);
                continue;
            }

            DrawHelpLine(lines[lineIdx], row, scrollLeft, size.Width);
        }

        // Footer key bar
        _screen.FillRegion(new Rect(0, size.Height - 1, size.Width, 1), PaletteStyles.KeyBarLabel(_palette));
        _screen.Write(0, size.Height - 1, "10", PaletteStyles.KeyBarNum(_palette));
        _screen.Write(2, size.Height - 1, "Close", PaletteStyles.KeyBarLabel(_palette));
    }

    private void DrawHelpLine(HelpLine line, int row, int scrollLeft, int width)
    {
        var bodyStyle = PaletteStyles.HelpBody(_palette);

        // Fill background first
        _screen.FillRegion(new Rect(0, row, width, 1), bodyStyle);

        switch (line.Kind)
        {
            case HelpLineKind.Empty:
                break;

            case HelpLineKind.Title:
                WriteClipped(0, row, line.Description, scrollLeft, width,
                    PaletteStyles.HelpHeading(_palette));
                break;

            case HelpLineKind.Separator:
                WriteClipped(0, row, line.Description, scrollLeft, width,
                    PaletteStyles.HelpSeparator(_palette));
                break;

            case HelpLineKind.Heading:
                WriteClipped(0, row, line.Description, scrollLeft, width,
                    PaletteStyles.HelpHeading(_palette));
                break;

            case HelpLineKind.KeyLine:
            {
                // "  KEY               Description"
                // Key column: 2 spaces indent + key text, padded to KeyColumnWidth
                string keyPart  = $"  {line.Key}";
                string descPart = line.Description;

                int keyDisplayWidth = KeyColumnWidth + 2; // "  " + key padded

                // Write key part (cyan)
                string keyPadded = keyPart.PadRight(keyDisplayWidth);
                WriteClipped(0, row, keyPadded, scrollLeft, width,
                    PaletteStyles.HelpKey(_palette));

                // Write description part (white) right after key column
                int descX = Math.Max(0, keyDisplayWidth - scrollLeft);
                if (descX < width && descPart.Length > 0)
                {
                    int descScrollLeft = Math.Max(0, scrollLeft - keyDisplayWidth);
                    string descVisible = descScrollLeft < descPart.Length
                        ? descPart[descScrollLeft..]
                        : string.Empty;
                    if (descVisible.Length > 0)
                        WriteClipped(descX, row, descVisible, 0, width - descX, bodyStyle);
                }
                break;
            }

            case HelpLineKind.Plain:
                WriteClipped(0, row, line.Description, scrollLeft, width, bodyStyle);
                break;
        }
    }

    private void WriteClipped(int x, int row, string text, int scrollLeft, int maxWidth, CellStyle style)
    {
        if (maxWidth <= 0 || text.Length == 0) return;
        string visible = scrollLeft < text.Length ? text[scrollLeft..] : string.Empty;
        if (visible.Length == 0) return;
        if (visible.Length > maxWidth) visible = visible[..maxWidth];
        _screen.Write(x, row, visible, style);
    }
}

