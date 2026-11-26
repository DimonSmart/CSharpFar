using CSharpFar.App.Menu;
using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal sealed class ApplicationServiceCallbacks
{
    public Action<FilePanelState, int> SafeRefresh { get; set; } =
        (_, _) => throw Missing();
    public Func<string, bool> CanExecuteFunctionKeyCommand { get; set; } =
        _ => throw Missing();
    public Func<MenuCommandRequest, ApplicationRuntimeRenderRequest> ExecuteMenuCommand { get; set; } =
        _ => throw Missing();
    public Action CaptureUnderlay { get; set; } = () => throw Missing();
    public Action StartWatchingInitialPanels { get; set; } = () => throw Missing();
    public Action RestoreTerminal { get; set; } = () => throw Missing();
    public Func<ApplicationUiInputPacket, ApplicationRuntimeRenderRequest> HandleApplicationInput { get; set; } =
        _ => throw Missing();
    public Action<PanelSide, CSharpFar.Module.Abstractions.IModulePanel> OpenModulePanel { get; set; } =
        (_, _) => throw Missing();

    private static InvalidOperationException Missing() =>
        new("Application service callback is not assigned.");
}
