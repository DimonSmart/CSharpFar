using CSharpFar.App.CommandLine;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Menu;
using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.App.Viewer;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationRenderContext
{
    public required TerminalSurfaceController TerminalSurface { get; init; }
    public required PanelController PanelController { get; init; }
    public required ApplicationState App { get; init; }
    public required UiTransientState Ui { get; init; }
    public required MenuState MenuState { get; init; }
    public required PanelQuickSearchController PanelQuickSearch { get; init; }
    public required CommandLineState CommandLine { get; init; }
    public required CommandCompletionState CommandCompletion { get; init; }
    public required Func<FilePanelState> LeftPanel { get; init; }
    public required Func<FilePanelState> RightPanel { get; init; }
    public required Func<PanelSide> ActiveSide { get; init; }
    public required Func<FilePanelState> ActiveState { get; init; }
    public required Func<PanelViewMode> LeftViewMode { get; init; }
    public required Func<PanelViewMode> RightViewMode { get; init; }
    public required Func<FunctionKeyLayer> FunctionKeyLayer { get; init; }
    public required Func<AppSettingsAlias.DirectoryShortcutSettings> DirectoryShortcuts { get; init; }
    public required QuickViewDirectorySizeController QuickViewDirectorySize { get; init; }
    public Func<MenuBarDefinition> BuildMenuDefinition { get; set; } = () => throw Missing();

    private static InvalidOperationException Missing() =>
        new("Application render context is not assigned.");
}
