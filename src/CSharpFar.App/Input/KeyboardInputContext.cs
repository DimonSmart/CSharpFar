using CSharpFar.App.CommandLine;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Input;

internal sealed class KeyboardInputContext
{
    public required PanelController PanelController { get; init; }
    public required CommandLineState CommandLine { get; init; }
    public required Action<PanelSide> SetActiveSide { get; init; }
    public required Func<FilePanelState> LeftPanel { get; init; }
    public required Func<FilePanelState> RightPanel { get; init; }
    public required Func<AppSettingsAlias.PanelOptionsSettings> PanelOptions { get; init; }
    public required Func<bool> QuickView { get; init; }
    public required Action<bool> SetQuickView { get; init; }
    public required Action<bool> SetRunning { get; init; }
    public Func<ConsoleModifiers, bool> SetFunctionKeyLayer { get; set; } = _ => throw Missing();
    public Func<string, object?, bool> ExecuteRegisteredCommand { get; set; } = (_, _) => throw Missing();
    public Action<PanelSide> ToggleSelectAllPanelItems { get; set; } = _ => throw Missing();
    public Func<bool> CopyCommandLineSelection { get; set; } = () => throw Missing();
    public Func<ApplicationWorkspaceMode, bool> PasteTextIntoCommandLine { get; set; } = _ => throw Missing();
    public Action OnVisibleCommandLineTextEdited { get; set; } = () => throw Missing();
    public Action<FilePanelState, PanelSide> CloseSearchResultsPanel { get; set; } =
        (_, _) => throw Missing();
    public Action<string> ExecuteCommand { get; set; } = _ => throw Missing();
    public Func<int, CommandHistoryNavigationStart, bool> BrowseCommandHistory { get; set; } =
        (_, _) => throw Missing();
    public Action<bool> HideCommandCompletion { get; set; } = _ => throw Missing();
    public Action ResetCommandHistoryNavigation { get; set; } = () => throw Missing();
    public Action<FilePanelState, PanelSide> TryGoUp { get; set; } = (_, _) => throw Missing();
    public Action<FilePanelState, PanelSide, FilePanelItem> OpenPanelItem { get; set; } = (_, _, _) => throw Missing();

    private static InvalidOperationException Missing() =>
        new("Keyboard input context is not assigned.");
}
