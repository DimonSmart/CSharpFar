using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Menu;
using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Bootstrap;

internal static class ApplicationRuntimeBuilder
{
    public static ApplicationRuntime Create(
        UiCompositionHost composition,
        ScreenRenderer screen,
        ApplicationUiSurface applicationSurface,
        ApplicationUiLayerScope applicationUiLayers,
        PendingMenuCommandQueue pendingMenuCommands,
        ApplicationSession session,
        ApplicationServiceCallbacks callbacks,
        PanelAutoRefreshService autoRefresh,
        QuickViewDirectorySizeController quickViewDirectorySize,
        FileUsagePanelController fileUsagePanel)
    {
        return new ApplicationRuntime(
            composition,
            screen,
            applicationSurface,
            applicationUiLayers,
            new ApplicationRuntimeContext
            {
                IsRunning = () => session.App.Running,
                WaitToken = () => autoRefresh.WaitToken,
                CaptureUnderlay = () => callbacks.CaptureUnderlay(),
                StartWatchingInitialPanels = () => callbacks.StartWatchingInitialPanels(),
                RestoreTerminal = () => callbacks.RestoreTerminal(),
                ResetWaitToken = autoRefresh.ResetWaitToken,
                ProcessPendingRefreshes = autoRefresh.ProcessPendingRefreshes,
                DisposeRuntimeState = () => { quickViewDirectorySize.Dispose(); fileUsagePanel.Dispose(); },
                HandleApplicationInput = packet => callbacks.HandleApplicationInput(packet),
                IsPanelsMode = () => session.App.WorkspaceMode == ApplicationWorkspaceMode.Panels,
                TryTakeMenuCommand = pendingMenuCommands.TryTake,
                ExecuteMenuCommand = request => callbacks.ExecuteMenuCommand(request),
            });
    }
}
