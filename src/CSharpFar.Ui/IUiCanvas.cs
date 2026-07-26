using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public interface IUiCanvas
{
    void Write(int x, int y, string text, CellStyle style);
    void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style);
    void WriteForced(int x, int y, string text, CellStyle style);
    void WriteForced(int x, int y, ReadOnlySpan<char> text, CellStyle style);
    void WriteChar(int x, int y, char ch, CellStyle style);
    void FillRegion(Rect region, CellStyle style);
    void DrawBox(Rect rect, CellStyle style);
    void DrawDoubleBox(Rect rect, CellStyle style);
    ConsoleSize Size { get; }
}

public static class UiCanvasExtensions
{
    public static IUiCanvas Clip(this IUiCanvas canvas, Rect bounds)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        return new ClippedUiCanvas(canvas, bounds);
    }
}

internal sealed class ClippedUiCanvas(IUiCanvas canvas, Rect bounds) : IUiCanvas
{
    public ConsoleSize Size => canvas.Size;

    public void Write(int x, int y, string text, CellStyle style) => WriteCore(x, y, text, style, forced: false);
    public void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style) => WriteCore(x, y, text.ToString(), style, forced: false);
    public void WriteForced(int x, int y, string text, CellStyle style) => WriteCore(x, y, text, style, forced: true);
    public void WriteForced(int x, int y, ReadOnlySpan<char> text, CellStyle style) => WriteCore(x, y, text.ToString(), style, forced: true);
    public void WriteChar(int x, int y, char ch, CellStyle style) => WriteCore(x, y, ch.ToString(), style, forced: false);

    public void FillRegion(Rect region, CellStyle style)
    {
        Rect clipped = Intersect(region, bounds);
        if (clipped.Width > 0 && clipped.Height > 0)
            canvas.FillRegion(clipped, style);
    }

    public void DrawBox(Rect rect, CellStyle style) => DrawBoxCore(rect, style, single: true);
    public void DrawDoubleBox(Rect rect, CellStyle style) => DrawBoxCore(rect, style, single: false);

    private void WriteCore(int x, int y, string text, CellStyle style, bool forced)
    {
        if (string.IsNullOrEmpty(text) || y < bounds.Y || y >= bounds.Bottom)
            return;

        int column = x;
        foreach (var rune in text.EnumerateRunes())
        {
            int runeWidth = ConsoleTextMetrics.GetCellWidth(rune);
            if (runeWidth == 0)
            {
                if (column > bounds.X && column <= bounds.Right)
                    WriteToCanvas(column, y, rune.ToString(), style, forced);
                continue;
            }

            if (column >= bounds.X && column + runeWidth <= bounds.Right)
                WriteToCanvas(column, y, rune.ToString(), style, forced);

            column += runeWidth;
            if (column >= bounds.Right)
                break;
        }
    }

    private void DrawBoxCore(Rect rect, CellStyle style, bool single)
    {
        if (rect.Width < 2 || rect.Height < 2)
            return;

        char topLeft = single ? '┌' : '╔';
        char topRight = single ? '┐' : '╗';
        char bottomLeft = single ? '└' : '╚';
        char bottomRight = single ? '┘' : '╝';
        char horizontal = single ? '─' : '═';
        char vertical = single ? '│' : '║';

        WriteChar(rect.X, rect.Y, topLeft, style);
        WriteChar(rect.Right - 1, rect.Y, topRight, style);
        WriteChar(rect.X, rect.Bottom - 1, bottomLeft, style);
        WriteChar(rect.Right - 1, rect.Bottom - 1, bottomRight, style);
        Write(rect.X + 1, rect.Y, new string(horizontal, rect.Width - 2), style);
        Write(rect.X + 1, rect.Bottom - 1, new string(horizontal, rect.Width - 2), style);
        for (int y = rect.Y + 1; y < rect.Bottom - 1; y++)
        {
            WriteChar(rect.X, y, vertical, style);
            WriteChar(rect.Right - 1, y, vertical, style);
        }
    }

    private void WriteToCanvas(int x, int y, string text, CellStyle style, bool forced)
    {
        if (forced)
            canvas.WriteForced(x, y, text, style);
        else
            canvas.Write(x, y, text, style);
    }

    private static Rect Intersect(Rect left, Rect right)
    {
        int x = Math.Max(left.X, right.X);
        int y = Math.Max(left.Y, right.Y);
        int endX = Math.Min(left.Right, right.Right);
        int endY = Math.Min(left.Bottom, right.Bottom);
        return new Rect(x, y, Math.Max(0, endX - x), Math.Max(0, endY - y));
    }
}

internal sealed class ScreenRendererCanvas(ScreenRenderer renderer) : IUiCanvas
{
    public void Write(int x, int y, string text, CellStyle style) => renderer.Write(x, y, text, style);
    public void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style) => renderer.Write(x, y, text, style);
    public void WriteForced(int x, int y, string text, CellStyle style) => renderer.WriteForced(x, y, text, style);
    public void WriteForced(int x, int y, ReadOnlySpan<char> text, CellStyle style) => renderer.WriteForced(x, y, text, style);
    public void WriteChar(int x, int y, char ch, CellStyle style) => renderer.WriteChar(x, y, ch, style);
    public void FillRegion(Rect region, CellStyle style) => renderer.FillRegion(region, style);
    public void DrawBox(Rect rect, CellStyle style) => renderer.DrawBox(rect, style);
    public void DrawDoubleBox(Rect rect, CellStyle style) => renderer.DrawDoubleBox(rect, style);
    public ConsoleSize Size => renderer.FrameViewport.Size;
}
