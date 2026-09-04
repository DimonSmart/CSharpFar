using CSharpFar.Core.Models;

namespace CSharpFar.App.State;

internal sealed class ApplicationState(CSharpFarPalette palette)
{
    public bool Running { get; set; } = true;

    public ApplicationWorkspaceMode WorkspaceMode { get; set; } =
        ApplicationWorkspaceMode.Panels;

    public bool QuickView { get; set; }
    public bool FileUsage { get; set; }
    public bool RestoreQuickViewAfterFileUsage { get; set; }

    public CSharpFarPalette Palette { get; set; } = palette;
}

internal enum ApplicationWorkspaceMode
{
    Panels,
    HiddenCommandLine,
}
