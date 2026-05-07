using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class CommandLineRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly CellStyle      _style;

    public CommandLineRenderer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _style  = palette is not null
            ? new CellStyle(palette.CommandLineFg, palette.CommandLineBg)
            : Theme.CommandLine;
    }

    public void Render(int y, int totalWidth, string currentDirectory, CommandLineState state)
    {
        _screen.FillRegion(new Rect(0, y, totalWidth, 1), _style);

        string prompt = currentDirectory + ">";
        string full   = prompt + state.Text;

        // If longer than screen width, show the tail (so the cursor stays visible)
        string display = full.Length <= totalWidth
            ? full
            : full[^totalWidth..];

        _screen.Write(0, y, display, _style);
    }

    /// <summary>
    /// Returns the screen X coordinate of the cursor within the command line row.
    /// Returns -1 if the cursor is scrolled off the left edge of the screen.
    /// </summary>
    public int GetCursorX(int totalWidth, string currentDirectory, CommandLineState state)
    {
        string prompt = currentDirectory + ">";
        string full   = prompt + state.Text;
        int rawX      = prompt.Length + state.CursorPosition;

        if (full.Length <= totalWidth)
            return rawX;

        int scrolled = full.Length - totalWidth;
        int adjusted = rawX - scrolled;
        return adjusted < 0 ? -1 : adjusted;
    }
}
