using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Viewer;
using CSharpFar.Console;

namespace CSharpFar.App.Bootstrap;

internal static class ApplicationRuntimeBuilder
{
    public static ApplicationRuntime Create(
        ScreenRenderer screen,
        ApplicationServiceCallbacks callbacks,
        PanelAutoRefreshService autoRefresh,
        QuickViewDirectorySizeController quickViewDirectorySize)
    {
        return new ApplicationRuntime(
            screen,
            new ApplicationRuntimeContext
            {
                IsRunning = () => callbacks.IsRunning(),
                WaitToken = () => autoRefresh.WaitToken,
                CaptureUnderlay = () => callbacks.CaptureUnderlay(),
                StartWatchingInitialPanels = () => callbacks.StartWatchingInitialPanels(),
                RenderUi = isResizeRecovery => callbacks.RenderUi(isResizeRecovery),
                RestoreTerminal = () => callbacks.RestoreTerminal(),
                ResetWaitToken = autoRefresh.ResetWaitToken,
                ProcessPendingRefreshes = autoRefresh.ProcessPendingRefreshes,
                DisposeRuntimeState = quickViewDirectorySize.Dispose,
                HandleResizeInput = () => callbacks.HandleResizeInput(),
                CheckViewportAfterInput = () => callbacks.CheckViewportAfterInput(),
                HandleKeyInput = key => callbacks.HandleKeyInput(key),
                HandleModifierInput = modifiers => callbacks.HandleModifierInput(modifiers),
                HandleMouseInput = mouse => callbacks.HandleMouseInput(mouse),
            });
    }
}
