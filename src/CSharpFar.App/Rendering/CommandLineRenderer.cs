using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class CommandLineRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly CellStyle _style;
    private readonly CellStyle _selectionStyle;

    public CommandLineRenderer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        palette ??= PaletteRegistry.Default;
        _style = PaletteStyles.CommandLine(palette);
        _selectionStyle = new CellStyle(_style.Background, _style.Foreground);
    }

    public void Render(int y, int totalWidth, string currentDirectory, CommandLineState state)
    {
        ApplicationCommandLineFrame frame = CommandLineLayoutCalculator.Calculate(y, totalWidth, currentDirectory, state);
        Render(frame, currentDirectory, state);
    }

    public void Render(ApplicationCommandLineFrame frame, string currentDirectory, CommandLineState state)
    {
        if (frame.Bounds.Width <= 0)
            return;

        _screen.FillRegion(frame.Bounds, _style);

        string prompt = currentDirectory + ">";
        string full = prompt + state.Text;
        int offset = frame.DisplayOffset;

        string display = full.Length > offset ? full[offset..] : string.Empty;
        if (display.Length > frame.Bounds.Width)
            display = display[..frame.Bounds.Width];
        display = display.PadRight(frame.Bounds.Width);

        if (!state.HasSelection)
        {
            _screen.Write(frame.Bounds.X, frame.Bounds.Y, display, _style);
            return;
        }

        // Render selection over the text portion, not the prompt.
        int selectionStartX = prompt.Length + state.SelectionStart!.Value - offset;
        int selectionEndX = selectionStartX + state.SelectionLength;
        for (int i = 0; i < display.Length; i++)
        {
            bool isSelected = i >= selectionStartX && i < selectionEndX;
            _screen.WriteChar(frame.Bounds.X + i, frame.Bounds.Y, display[i], isSelected ? _selectionStyle : _style);
        }
    }

    /// <summary>
    /// Returns the screen X coordinate of the cursor within the command line row.
    /// Returns -1 if the cursor is scrolled off the left edge of the screen.
    /// </summary>
    public int GetCursorX(int totalWidth, string currentDirectory, CommandLineState state)
    {
        var frame = CommandLineLayoutCalculator.Calculate(0, totalWidth, currentDirectory, state);
        return frame.Cursor?.X ?? -1;
    }
}
