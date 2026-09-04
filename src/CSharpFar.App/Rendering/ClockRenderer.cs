using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ClockRenderer
{
    private readonly Func<CSharpFarPalette> _palette;
    private readonly Func<DateTime> _now;

    public ClockRenderer(Func<CSharpFarPalette> palette, Func<DateTime>? now = null)
    {
        _palette = palette;
        _now = now ?? (() => DateTime.Now);
    }

    public ApplicationClockFrame? CreateFrame(ConsoleSize size)
    {
        string text = _now().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (text.Length > size.Width)
            return null;

        return new ApplicationClockFrame(
            new Rect(size.Width - text.Length, 0, text.Length, size.Height > 0 ? 1 : 0),
            text);
    }

    public ApplicationClockFrame? Render(IUiCanvas canvas, ConsoleSize size)
    {
        ApplicationClockFrame? frame = CreateFrame(size);
        if (frame is null || frame.Bounds.Height <= 0)
            return frame;

        var palette = _palette();
        var style = new CellStyle(palette.PanelPathActiveFg, palette.PanelPathActiveBg);
        canvas.Write(frame.Bounds.X, frame.Bounds.Y, frame.Text, style);
        return frame;
    }
}

internal sealed record ApplicationClockFrame(Rect Bounds, string Text);
