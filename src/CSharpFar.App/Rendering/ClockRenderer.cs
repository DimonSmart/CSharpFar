using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ClockRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly Func<ConsolePalette> _palette;

    public ClockRenderer(ScreenRenderer screen, Func<ConsolePalette> palette)
    {
        _screen = screen;
        _palette = palette;
    }

    public void Render(ConsoleSize size)
    {
        string text = DateTime.Now.ToString("H:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (text.Length > size.Width)
            return;

        var palette = _palette();
        var style = new CellStyle(palette.PanelPathActiveFg, palette.PanelPathActiveBg);
        _screen.Write(size.Width - text.Length, 0, text, style);
    }
}
