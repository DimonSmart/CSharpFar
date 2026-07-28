using CSharpFar.Console.Models;
using SkiaSharp;

namespace CSharpFar.DemoRecorder;

internal sealed class SnapshotRasterizer : IDisposable
{
    private static readonly SKColor DefaultBackground = new(12, 12, 12);

    private readonly DemoRenderOptions _options;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _backgroundPaint;
    private readonly SKFont _font;
    private readonly float _baselineOffset;

    public SnapshotRasterizer(DemoRenderOptions options)
    {
        _options = options;

        SKTypeface typeface = CreateTypeface(options.FontFamily);
        _font = new SKFont(typeface, options.FontSize)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
        };

        _textPaint = new SKPaint
        {
            IsAntialias = true,
        };
        _backgroundPaint = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
        };

        SKFontMetrics metrics = _font.Metrics;
        _baselineOffset = _options.CellHeight - ((_options.CellHeight - (metrics.Descent - metrics.Ascent)) / 2f) - metrics.Descent;
    }

    public void SaveFrame(
        ScreenSnapshot snapshot,
        int cursorX,
        int cursorY,
        bool cursorVisible,
        string outputPath)
    {
        using SKBitmap bitmap = RenderBitmap(snapshot, cursorX, cursorY, cursorVisible);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    public void SavePng(
        ScreenSnapshot snapshot,
        int cursorX,
        int cursorY,
        bool cursorVisible,
        string outputPath) =>
        SaveFrame(snapshot, cursorX, cursorY, cursorVisible, outputPath);

    public void Dispose()
    {
        _font.Dispose();
        _textPaint.Dispose();
        _backgroundPaint.Dispose();
    }

    private SKBitmap RenderBitmap(
        ScreenSnapshot snapshot,
        int cursorX,
        int cursorY,
        bool cursorVisible)
    {
        int width = snapshot.Region.Width * _options.CellWidth + (_options.Padding * 2);
        int height = snapshot.Region.Height * _options.CellHeight + (_options.Padding * 2);
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using SKCanvas canvas = new(bitmap);
        canvas.Clear(DefaultBackground);

        for (int row = 0; row < snapshot.Region.Height; row++)
        {
            for (int col = 0; col < snapshot.Region.Width; col++)
            {
                SnapshotCell cell = snapshot.Cells[row, col];
                int left = _options.Padding + col * _options.CellWidth;
                int top = _options.Padding + row * _options.CellHeight;
                var rect = new SKRect(left, top, left + _options.CellWidth, top + _options.CellHeight);

                bool drawCursor = cursorVisible &&
                    cursorX == snapshot.Region.X + col &&
                    cursorY == snapshot.Region.Y + row;

                SKColor background = Map(cell.Background);
                SKColor foreground = Map(cell.Foreground);
                if (drawCursor)
                    (foreground, background) = (background, foreground);

                _backgroundPaint.Color = background;
                canvas.DrawRect(rect, _backgroundPaint);

                if (cell.Character == ' ')
                    continue;

                _textPaint.Color = foreground;
                canvas.DrawText(
                    cell.Character.ToString(),
                    left + TextInsetX(),
                    top + _baselineOffset,
                    SKTextAlign.Left,
                    _font,
                    _textPaint);
            }
        }

        return bitmap;
    }

    private float TextInsetX() => MathF.Max(0f, (_options.CellWidth - _font.MeasureText("W")) / 2f);

    private static SKTypeface CreateTypeface(string fontFamily)
    {
        SKTypeface? preferred = SKTypeface.FromFamilyName(fontFamily);
        if (preferred is not null)
            return preferred;

        return SKTypeface.FromFamilyName("Courier New") ??
               SKTypeface.FromFamilyName("Liberation Mono") ??
               SKTypeface.Default;
    }

    private static SKColor Map(ConsoleColor color) => color switch
    {
        ConsoleColor.Black => new SKColor(12, 12, 12),
        ConsoleColor.DarkBlue => new SKColor(0, 0, 128),
        ConsoleColor.DarkGreen => new SKColor(0, 100, 0),
        ConsoleColor.DarkCyan => new SKColor(0, 139, 139),
        ConsoleColor.DarkRed => new SKColor(139, 0, 0),
        ConsoleColor.DarkMagenta => new SKColor(139, 0, 139),
        ConsoleColor.DarkYellow => new SKColor(184, 134, 11),
        ConsoleColor.Gray => new SKColor(204, 204, 204),
        ConsoleColor.DarkGray => new SKColor(118, 118, 118),
        ConsoleColor.Blue => new SKColor(59, 120, 255),
        ConsoleColor.Green => new SKColor(22, 198, 12),
        ConsoleColor.Cyan => new SKColor(97, 214, 214),
        ConsoleColor.Red => new SKColor(231, 72, 86),
        ConsoleColor.Magenta => new SKColor(180, 0, 158),
        ConsoleColor.Yellow => new SKColor(249, 241, 165),
        ConsoleColor.White => new SKColor(242, 242, 242),
        _ => new SKColor(242, 242, 242),
    };
}
