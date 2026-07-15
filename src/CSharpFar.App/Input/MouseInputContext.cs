using CSharpFar.App.CommandLine;
using CSharpFar.App.Panels;
using CSharpFar.App.State;
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
    public required Func<AppSettingsAlias.PanelOptionsSettings> PanelOptions { get; init; }
    public required Action<PanelSide> SetActiveSide { get; init; }
    public required Func<PanelSide, FilePanelState> GetPanelState { get; init; }
    public Func<string, object?, bool> ExecuteRegisteredCommand { get; set; } = (_, _) => throw Missing();
    public Func<bool> PasteTextIntoCommandLine { get; set; } = () => throw Missing();
    public Action ResetCommandHistoryNavigation { get; set; } = () => throw Missing();
    public Action<FilePanelState, int> SafeRefresh { get; set; } = (_, _) => throw Missing();
    public Action<FilePanelState, PanelSide, FilePanelItem> OpenPanelItem { get; set; } =
        (_, _, _) => throw Missing();

    private static InvalidOperationException Missing() =>
        new("Mouse input context is not assigned.");
}
