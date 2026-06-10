using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationCommandLineRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly Func<ConsolePalette> _palette;

    public ApplicationCommandLineRenderer(ScreenRenderer screen, Func<ConsolePalette> palette)
    {
        _screen = screen;
        _palette = palette;
    }

    public void Render(int row, ConsoleSize size, string currentDirectory, CommandLineState commandLine) =>
        CreateRenderer().Render(row, size.Width, currentDirectory, commandLine);

    public void PositionCursor(int row, ConsoleSize size, string currentDirectory, CommandLineState commandLine)
    {
        int cursorX = CreateRenderer().GetCursorX(size.Width, currentDirectory, commandLine);
        if (cursorX >= 0 && cursorX < size.Width)
        {
            _screen.SetCursorPosition(cursorX, row);
            _screen.SetCursorVisible(true);
        }
    }

    private CommandLineRenderer CreateRenderer() =>
        new(_screen, _palette());
}
