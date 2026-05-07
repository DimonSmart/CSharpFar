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
        if (totalWidth <= 0)
            return;

        _screen.FillRegion(new Rect(0, y, totalWidth, 1), _style);

        string prompt = currentDirectory + ">";
        string full   = prompt + state.Text;
        int offset    = GetDisplayOffset(totalWidth, prompt.Length, full.Length, state.CursorPosition);

        string display = full.Length <= totalWidth
            ? full
            : full.Substring(offset, totalWidth);

        _screen.Write(0, y, display, _style);
    }

    /// <summary>
    /// Returns the screen X coordinate of the cursor within the command line row.
    /// Returns -1 if the cursor is scrolled off the left edge of the screen.
    /// </summary>
    public int GetCursorX(int totalWidth, string currentDirectory, CommandLineState state)
    {
        if (totalWidth <= 0)
            return -1;

        string prompt = currentDirectory + ">";
        string full   = prompt + state.Text;
        int rawX      = prompt.Length + state.CursorPosition;
        int offset    = GetDisplayOffset(totalWidth, prompt.Length, full.Length, state.CursorPosition);

        int adjusted = rawX - offset;
        return adjusted < 0 ? -1 : Math.Min(adjusted, totalWidth - 1);
    }

    private static int GetDisplayOffset(
        int totalWidth,
        int promptLength,
        int fullLength,
        int cursorPosition)
    {
        if (fullLength <= totalWidth)
            return 0;

        int rawCursorX = promptLength + cursorPosition;
        int maxOffset = fullLength - totalWidth;
        return Math.Clamp(rawCursorX - totalWidth + 1, 0, maxOffset);
    }
}
