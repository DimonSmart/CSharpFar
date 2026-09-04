using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationCommandLineRenderer
{
    private readonly Func<CSharpFarPalette> _palette;

    public ApplicationCommandLineRenderer(Func<CSharpFarPalette> palette)
    {
        _palette = palette;
    }

    public ApplicationCommandLineFrame Render(
        IUiCanvas canvas,
        int row,
        ConsoleSize size,
        string currentDirectory,
        CommandLineState commandLine)
    {
        ApplicationCommandLineFrame frame = CommandLineLayoutCalculator.Calculate(row, size.Width, currentDirectory, commandLine);
        CreateRenderer(canvas).Render(frame, currentDirectory, commandLine);
        return frame;
    }

    public void Render(
        IUiCanvas canvas,
        ApplicationCommandLineFrame frame,
        string currentDirectory,
        CommandLineState commandLine) =>
        CreateRenderer(canvas).Render(frame, currentDirectory, commandLine);

    private CommandLineRenderer CreateRenderer(IUiCanvas canvas) =>
        new(canvas, _palette());
}
