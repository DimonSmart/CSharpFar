using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed partial class HelpViewerLayer
{
    private static void Draw(
        IUiCanvas screen,
        HelpLine[] lines,
        HelpViewerFrame frame,
        ConsolePalette palette)
    {
        int width = frame.Viewport.Size.Width;
        int height = frame.Viewport.Size.Height;
        if (width <= 0 || height <= 0)
            return;

        string pos = lines.Length == 0 ? " 0/0 " : $" {frame.ScrollTop + 1}/{lines.Length} ";
        int nameWidth = Math.Max(0, width - pos.Length);
        screen.Write(
            0,
            0,
            (" CSharpFar Help ".PadRight(nameWidth)[..nameWidth] + pos)[..width],
            PaletteStyles.PathHeaderActive(palette));
        CellStyle body = PaletteStyles.HelpBody(palette);
        for (int row = 0; row < frame.VisibleRows; row++)
        {
            int line = frame.ScrollTop + row;
            screen.FillRegion(new Rect(0, row + 1, width, 1), body);
            if (line < lines.Length)
                DrawLine(screen, lines[line], row + 1, frame.ScrollLeft, frame.ContentBounds.Width, palette);
        }

        if (frame.ScrollBarBounds is { } scrollbar && frame.VerticalScrollState is { } scrollState)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                screen,
                scrollbar,
                scrollState,
                new ScrollBarOptions { Enabled = true },
                body);
        }

        FunctionKeysController.Render(
            screen,
            frame.FunctionKeyBarBounds.Y,
            frame.FunctionKeyBarBounds.Width,
            FunctionKeyActions);
    }

    private static void DrawLine(
        IUiCanvas screen,
        HelpLine line,
        int y,
        int left,
        int width,
        ConsolePalette palette)
    {
        if (width <= 0)
            return;

        if (line.Kind == HelpLineKind.KeyLine)
        {
            WriteClipped(
                screen,
                0,
                y,
                $"  {line.Key}".PadRight(KeyColumnWidth),
                0,
                left,
                width,
                PaletteStyles.HelpKey(palette));
            WriteClipped(
                screen,
                0,
                y,
                line.Description,
                KeyColumnWidth,
                left,
                width,
                PaletteStyles.HelpBody(palette));
            return;
        }

        CellStyle style = line.Kind switch
        {
            HelpLineKind.Title or HelpLineKind.Heading => PaletteStyles.HelpHeading(palette),
            HelpLineKind.Separator => PaletteStyles.HelpSeparator(palette),
            _ => PaletteStyles.HelpBody(palette),
        };
        WriteClipped(screen, 0, y, line.Description, 0, left, width, style);
    }

    private static void WriteClipped(
        IUiCanvas screen,
        int x,
        int y,
        string text,
        int textStart,
        int left,
        int width,
        CellStyle style)
    {
        int visibleStart = Math.Max(textStart, left);
        int visibleEnd = Math.Min(textStart + text.Length, left + width);
        if (visibleStart >= visibleEnd)
            return;

        int sourceStart = visibleStart - textStart;
        int count = visibleEnd - visibleStart;
        screen.Write(x + visibleStart - left, y, text.Substring(sourceStart, count), style);
    }

    private static int MaximumDisplayWidth(IEnumerable<HelpLine> lines) =>
        lines.Select(DisplayWidth).DefaultIfEmpty(0).Max();

    private static int DisplayWidth(HelpLine line) =>
        line.Kind == HelpLineKind.KeyLine
            ? KeyColumnWidth + line.Description.Length
            : line.Description.Length;
}
