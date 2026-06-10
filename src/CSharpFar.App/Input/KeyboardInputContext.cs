using CSharpFar.App.CommandLine;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Menu;
using CSharpFar.App.Panels;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Input;

internal sealed class KeyboardInputContext
{
    public required MenuState MenuState { get; init; }
    public TopMenuController MenuController { get; set; } = null!;
    public PanelQuickSearchController PanelQuickSearch { get; set; } = null!;
    public required PanelController PanelController { get; init; }
    public required CommandLineState CommandLine { get; init; }
    public required IReadOnlyList<FunctionKeyBinding> FunctionKeyBindings { get; init; }
    public Func<MenuBarDefinition> BuildMenuDefinition { get; set; } = () => throw Missing();
    public required Func<PanelSide> ActiveSide { get; init; }
    public required Action<PanelSide> SetActiveSide { get; init; }
    public required Func<FilePanelState> ActiveState { get; init; }
    public required Func<FilePanelState> LeftPanel { get; init; }
    public required Func<FilePanelState> RightPanel { get; init; }
    public required Func<bool> HasVisiblePanels { get; init; }
    public required Func<PanelSide, bool> IsPanelVisible { get; init; }
    public required Func<AppSettingsAlias.PanelOptionsSettings> PanelOptions { get; init; }
    public required Func<int> VisibleRows { get; init; }
    public required Func<PanelSide, int> VisibleRowsForSide { get; init; }
    public required Func<bool> QuickView { get; init; }
    public required Action<bool> SetQuickView { get; init; }
    public required Action<bool> SetRunning { get; init; }
    public Func<bool> TogglePanels { get; set; } = () => throw Missing();
    public Func<ConsoleKeyInfo, bool> TryHandleFarNetPanelShortcut { get; set; } = _ => throw Missing();
    public Func<string, object?, bool> ExecuteRegisteredCommand { get; set; } = (_, _) => throw Missing();
    public Action SelectAllCommandLineTextOrPanelItems { get; set; } = () => throw Missing();
    public Func<bool> CopyCommandLineSelection { get; set; } = () => throw Missing();
    public Func<bool> PasteTextIntoCommandLine { get; set; } = () => throw Missing();
    public Action<int> MovePanelColumn { get; set; } = _ => throw Missing();
    public Action OnVisibleCommandLineTextEdited { get; set; } = () => throw Missing();
    public Func<bool> TryHideCommandCompletionTemporarily { get; set; } = () => throw Missing();
    public Action<FilePanelState, PanelSide> CloseSearchResultsPanel { get; set; } =
        (_, _) => throw Missing();
    public Func<bool> TryAcceptCommandCompletion { get; set; } = () => throw Missing();
    public Action<string> ExecuteCommand { get; set; } = _ => throw Missing();
    public Action EnsureActivePanelVisible { get; set; } = () => throw Missing();
    public Func<int, bool> TryMoveCommandCompletionSelection { get; set; } = _ => throw Missing();
    public Func<int, CommandHistoryNavigationStart, bool> BrowseCommandHistory { get; set; } =
        (_, _) => throw Missing();
    public Action<bool> HideCommandCompletion { get; set; } = _ => throw Missing();
    public Action ResetCommandHistoryNavigation { get; set; } = () => throw Missing();
    public Action TryGoUp { get; set; } = () => throw Missing();
    public Func<string, bool> CanExecuteFunctionKeyCommand { get; set; } = _ => throw Missing();

    private static InvalidOperationException Missing() =>
        new("Keyboard input context is not assigned.");
}
