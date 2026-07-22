using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationCommandLineRenderer
{
    private readonly IUiCanvas _screen;
    private readonly Func<ConsolePalette> _palette;

    public ApplicationCommandLineRenderer(IUiCanvas screen, Func<ConsolePalette> palette)
    {
        _screen = screen;
        _palette = palette;
    }

    public ApplicationCommandLineFrame Render(
        int row,
        ConsoleSize size,
        string currentDirectory,
        CommandLineState commandLine)
    {
        ApplicationCommandLineFrame frame = CommandLineLayoutCalculator.Calculate(row, size.Width, currentDirectory, commandLine);
        CreateRenderer().Render(frame, currentDirectory, commandLine);
        return frame;
    }

    private CommandLineRenderer CreateRenderer() =>
        new(_screen, _palette());
}
