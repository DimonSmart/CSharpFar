using CSharpFar.Core.Models;

namespace CSharpFar.App.State;

internal sealed class ApplicationState(ConsolePalette palette)
{
    public bool Running { get; set; } = true;

    public HiddenPanels HiddenPanels { get; set; }

    public bool QuickView { get; set; }

    public ConsolePalette Palette { get; set; } = palette;
}

[Flags]
internal enum HiddenPanels
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = Left | Right,
}
