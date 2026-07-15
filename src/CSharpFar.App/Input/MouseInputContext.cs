using CSharpFar.App.CommandLine;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.Console.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Input;

internal sealed class MouseInputContext
{
    public required PanelController PanelController { get; init; }
    public required CommandLineState CommandLine { get; init; }
    public required UiTransientState Ui { get; init; }
    public required MouseSessionState Mouse { get; init; }
    public required IReadOnlyList<FunctionKeyBinding> FunctionKeyBindings { get; init; }
    public required Func<FunctionKeyLayer> FunctionKeyLayer { get; init; }
    public required Func<AppSettingsAlias.DirectoryShortcutSettings> DirectoryShortcuts { get; init; }
    public required Func<AppSettingsAlias.PanelOptionsSettings> PanelOptions { get; init; }
    public required Func<ConsoleSize> LastRenderSizeOrCurrent { get; init; }
    public required Func<PanelSide> ActiveSide { get; init; }
    public required Action<PanelSide> SetActiveSide { get; init; }
    public required Func<FilePanelState> ActiveState { get; init; }
    public required Func<PanelSide, FilePanelState> GetPanelState { get; init; }
    public required Func<PanelSide, PanelViewMode> ViewModeForSide { get; init; }
    public required Func<PanelSide, bool> IsPanelVisible { get; init; }
    public required Func<bool> HasVisiblePanels { get; init; }
    public required Func<bool> QuickView { get; init; }
    public required Func<PanelSide, int> VisibleRowsForSide { get; init; }
    public Func<string, object?, bool> ExecuteRegisteredCommand { get; set; } = (_, _) => throw Missing();
    public Func<string, bool> CanExecuteFunctionKeyCommand { get; set; } = _ => throw Missing();
    public Func<bool> PasteTextIntoCommandLine { get; set; } = () => throw Missing();
    public Action ResetCommandHistoryNavigation { get; set; } = () => throw Missing();
    public Action<bool> HideCommandCompletion { get; set; } = _ => throw Missing();
    public Action<FilePanelState, int> SafeRefresh { get; set; } = (_, _) => throw Missing();
    public Action<FilePanelState, PanelSide, FilePanelItem> OpenPanelItem { get; set; } =
        (_, _, _) => throw Missing();

    private static InvalidOperationException Missing() =>
        new("Mouse input context is not assigned.");
}
