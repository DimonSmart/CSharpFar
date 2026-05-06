using CSharpFar.Console.Models;

namespace CSharpFar.Console;

/// <summary>
/// Higher-level rendering surface built on top of <see cref="IConsoleDriver"/>.
/// Provides convenience methods for drawing text, boxes, and regions.
/// </summary>
public sealed class ScreenRenderer
{
    private readonly IConsoleDriver _driver;

    public ScreenRenderer(IConsoleDriver driver)
    {
        _driver = driver;
    }

    public ConsoleSize GetSize() => _driver.GetSize();

    public void Write(int x, int y, string text, CellStyle style) =>
        _driver.WriteAt(x, y, text.AsSpan(), style.Foreground, style.Background);

    public void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style) =>
        _driver.WriteAt(x, y, text, style.Foreground, style.Background);

    public void WriteChar(int x, int y, char ch, CellStyle style) =>
        _driver.WriteAt(x, y, stackalloc char[] { ch }, style.Foreground, style.Background);

    /// <summary>Fills a region with spaces using the given style.</summary>
    public void FillRegion(Rect region, CellStyle style)
    {
        var size = _driver.GetSize();
        int y1 = Math.Max(0, region.Y);
        int y2 = Math.Min(size.Height, region.Bottom);
        int x1 = Math.Max(0, region.X);
        int x2 = Math.Min(size.Width, region.Right);
        int w = x2 - x1;

        if (w <= 0 || y2 <= y1)
            return;

        var spaces = new string(' ', w);
        for (int y = y1; y < y2; y++)
            _driver.WriteAt(x1, y, spaces.AsSpan(), style.Foreground, style.Background);
    }

    public void ClearRegion(Rect region) =>
        _driver.ClearRegion(region);

    public void ClearScreen()
    {
        var size = _driver.GetSize();
        _driver.ClearRegion(new Rect(0, 0, size.Width, size.Height));
    }

    /// <summary>Draws a single-line box border.</summary>
    public void DrawBox(Rect rect, CellStyle style)
    {
        if (rect.Width < 2 || rect.Height < 2)
            return;

        int x = rect.X;
        int y = rect.Y;
        int w = rect.Width;
        int h = rect.Height;

        // Corners
        WriteChar(x, y, '┌', style);
        WriteChar(x + w - 1, y, '┐', style);
        WriteChar(x, y + h - 1, '└', style);
        WriteChar(x + w - 1, y + h - 1, '┘', style);

        // Horizontal lines
        var hLine = new string('─', w - 2);
        Write(x + 1, y, hLine, style);
        Write(x + 1, y + h - 1, hLine, style);

        // Vertical lines
        for (int row = y + 1; row < y + h - 1; row++)
        {
            WriteChar(x, row, '│', style);
            WriteChar(x + w - 1, row, '│', style);
        }
    }

    public void SetCursorPosition(int x, int y) => _driver.SetCursorPosition(x, y);
    public void SetCursorVisible(bool visible) => _driver.SetCursorVisible(visible);

    public ConsoleKeyInfo ReadKey() => _driver.ReadKey(true);

    public ScreenSnapshot Capture(Rect region) => _driver.Capture(region);
    public void Restore(ScreenSnapshot snapshot) => _driver.Restore(snapshot);
}
