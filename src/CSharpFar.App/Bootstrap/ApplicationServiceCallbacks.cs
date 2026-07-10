using CSharpFar.App.Menu;
using CSharpFar.Console.Input;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal sealed class ApplicationServiceCallbacks
{
    public Func<FilePanelState> ActiveState { get; set; } = () => throw Missing();
    public Func<PanelSide> GetActiveSide { get; set; } = () => throw Missing();
    public Action<PanelSide> SetActiveSide { get; set; } = _ => throw Missing();
    public Action<bool> SetQuickView { get; set; } = _ => throw Missing();
    public Func<AppSettingsAlias.PanelOptionsSettings> PanelOptions { get; set; } =
        () => throw Missing();
    public Func<PanelSide, FilePanelState> GetPanelState { get; set; } =
        _ => throw Missing();
    public Func<FilePanelState, PanelSide> PanelSideForState { get; set; } =
        _ => throw Missing();
    public Func<int> VisibleRows { get; set; } = () => throw Missing();
    public Func<PanelSide, int> VisibleRowsForSide { get; set; } = _ => throw Missing();
    public Action<FilePanelState, PanelSide> StartWatching { get; set; } =
        (_, _) => throw Missing();
    public Action<FilePanelState, int> SafeRefresh { get; set; } =
        (_, _) => throw Missing();
    public Action<FilePanelState> ClosePanelQuickSearchForState { get; set; } =
        _ => throw Missing();
    public Action<PanelSide> ClosePanelQuickSearchForPanel { get; set; } =
        _ => throw Missing();
    public Func<bool> HasVisiblePanels { get; set; } = () => throw Missing();
    public Func<PanelSide, bool> IsPanelVisible { get; set; } = _ => throw Missing();
    public Action<FilePanelState, FilePanelItem> ViewPanelFile { get; set; } =
        (_, _) => throw Missing();
    public Action<string, string, Action> ExecuteInCurrentConsole { get; set; } =
        (_, _, _) => throw Missing();
    public Func<string, bool> CanExecuteFunctionKeyCommand { get; set; } =
        _ => throw Missing();
    public Func<MenuCommandRequest, MenuCommandResult> ExecuteMenuCommand { get; set; } =
        _ => throw Missing();
    public Func<bool> IsRunning { get; set; } = () => throw Missing();
    public Action CaptureUnderlay { get; set; } = () => throw Missing();
    public Action StartWatchingInitialPanels { get; set; } = () => throw Missing();
    public Action<bool> RenderUi { get; set; } = _ => throw Missing();
    public Action RestoreTerminal { get; set; } = () => throw Missing();
    public Func<ApplicationRuntimeRenderRequest> HandleResizeInput { get; set; } =
        () => throw Missing();
    public Func<ApplicationRuntimeRenderRequest> CheckViewportAfterInput { get; set; } =
        () => throw Missing();
    public Func<ConsoleKeyInfo, ApplicationRuntimeRenderRequest> HandleKeyInput { get; set; } =
        _ => throw Missing();
    public Func<ConsoleModifiers, ApplicationRuntimeRenderRequest> HandleModifierInput { get; set; } =
        _ => throw Missing();
    public Func<MouseConsoleInputEvent, ApplicationRuntimeRenderRequest> HandleMouseInput { get; set; } =
        _ => throw Missing();
    public Action RefreshPanels { get; set; } = () => throw Missing();
    public Action<PanelSide, CSharpFar.Module.Abstractions.IModulePanel> OpenModulePanel { get; set; } =
        (_, _) => throw Missing();

    private static InvalidOperationException Missing() =>
        new("Application service callback is not assigned.");
}
