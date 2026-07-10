using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Bootstrap;

internal static class ApplicationRuntimeBuilder
{
    public static ApplicationRuntime Create(
        UiCompositionHost composition,
        ApplicationServiceCallbacks callbacks,
        PanelAutoRefreshService autoRefresh,
        QuickViewDirectorySizeController quickViewDirectorySize)
    {
        return new ApplicationRuntime(
            composition,
            new ApplicationRuntimeContext
            {
                IsRunning = () => callbacks.IsRunning(),
                WaitToken = () => autoRefresh.WaitToken,
                CaptureUnderlay = () => callbacks.CaptureUnderlay(),
                StartWatchingInitialPanels = () => callbacks.StartWatchingInitialPanels(),
                RestoreTerminal = () => callbacks.RestoreTerminal(),
                ResetWaitToken = autoRefresh.ResetWaitToken,
                ProcessPendingRefreshes = autoRefresh.ProcessPendingRefreshes,
                DisposeRuntimeState = quickViewDirectorySize.Dispose,
                HandleKeyInput = key => callbacks.HandleKeyInput(key),
                HandleModifierInput = modifiers => callbacks.HandleModifierInput(modifiers),
                HandleMouseInput = mouse => callbacks.HandleMouseInput(mouse),
            });
    }
}
