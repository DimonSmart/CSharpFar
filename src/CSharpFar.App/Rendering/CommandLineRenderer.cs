using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class CommandLineRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly CellStyle      _style;
    private readonly CellStyle      _selectionStyle;

    public CommandLineRenderer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen         = screen;
        palette        ??= PaletteRegistry.Default;
        _style          = PaletteStyles.CommandLine(palette);
        _selectionStyle = new CellStyle(_style.Background, _style.Foreground);
    }

    public void Render(int y, int totalWidth, string currentDirectory, CommandLineState state)
    {
        if (totalWidth <= 0)
            return;

        _screen.FillRegion(new Rect(0, y, totalWidth, 1), _style);

        string prompt = currentDirectory + ">";
        string full   = prompt + state.Text;
        int offset    = GetDisplayOffset(totalWidth, prompt.Length, full.Length, state.CursorPosition);

        string display = full.Length > offset ? full[offset..] : string.Empty;
        if (display.Length > totalWidth)
            display = display[..totalWidth];
        display = display.PadRight(totalWidth);

        if (!state.HasSelection)
        {
            _screen.Write(0, y, display, _style);
            return;
        }

        // Render selection over the text portion, not the prompt.
        int selectionStartX = prompt.Length + state.SelectionStart!.Value - offset;
        int selectionEndX = selectionStartX + state.SelectionLength;
        for (int i = 0; i < display.Length; i++)
        {
            bool isSelected = i >= selectionStartX && i < selectionEndX;
            _screen.WriteChar(i, y, display[i], isSelected ? _selectionStyle : _style);
        }
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
        return adjusted < 0 || adjusted >= totalWidth ? -1 : adjusted;
    }

    private static int GetDisplayOffset(
        int totalWidth,
        int promptLength,
        int fullLength,
        int cursorPosition)
    {
        if (fullLength < totalWidth)
            return 0;

        int rawCursorX = promptLength + cursorPosition;
        int maxOffset = Math.Max(0, fullLength - totalWidth + 1);
        return Math.Clamp(rawCursorX - totalWidth + 1, 0, maxOffset);
    }
}
