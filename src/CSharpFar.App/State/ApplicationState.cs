using CSharpFar.Core.Models;

namespace CSharpFar.App.State;

internal sealed class ApplicationState(ConsolePalette palette)
{
    public bool Running { get; set; } = true;

    public ApplicationWorkspaceMode WorkspaceMode { get; set; } =
        ApplicationWorkspaceMode.Panels;

    public bool QuickView { get; set; }

    public ConsolePalette Palette { get; set; } = palette;
}

internal enum ApplicationWorkspaceMode
{
    Panels,
    HiddenCommandLine,
}
