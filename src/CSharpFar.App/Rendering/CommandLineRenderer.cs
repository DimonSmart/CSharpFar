using CSharpFar.Console;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class CommandLineRenderer
{
    private readonly IUiCanvas _screen;
    private readonly CellStyle _style;
    private readonly CellStyle _selectionStyle;

    public CommandLineRenderer(IUiCanvas screen, CSharpFarPalette? palette = null)
    {
        _screen = screen;
        palette ??= CSharpFarPaletteRegistry.Default;
        _style = CSharpFarPaletteStyles.CommandLine(palette);
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

        SingleLineTextEditState presentation = CommandLinePresentationState.Create(currentDirectory, state);
        SingleLineTextInput.Render(
            _screen,
            frame.Bounds.X,
            frame.Bounds.Y,
            frame.Bounds.Width,
            presentation,
            _style,
            _selectionStyle);
    }

    public int GetCursorX(int totalWidth, string currentDirectory, CommandLineState state)
    {
        if (totalWidth <= 0)
            return -1;

        SingleLineTextEditState presentation = CommandLinePresentationState.Create(currentDirectory, state);
        int x = SingleLineTextInput.GetCursorX(0, totalWidth, presentation);
        return x >= 0 && x < totalWidth ? x : -1;
    }
}
