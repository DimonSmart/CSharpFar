using CSharpFar.App;
using CSharpFar.App.Commands;

namespace CSharpFar.Tests;

/// <summary>Drive the real UI completion queue at an explicit test barrier.
/// Production input remains responsive while providers are running.</summary>
internal static class PanelLoadTestExtensions
{
    public static void WaitForPanelLoads(this ApplicationCommandContext context)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (context.LeftPanel.IsLoading || context.RightPanel.IsLoading)
        {
            context.ResetUiWakeToken();
            context.DrainUiActions?.Invoke();
            if (!context.LeftPanel.IsLoading && !context.RightPanel.IsLoading)
                break;
            int result = WaitHandle.WaitAny([context.UiWakeToken.WaitHandle, timeout.Token.WaitHandle]);
            if (result == 1)
                throw new TimeoutException("Panel loading did not complete at the test barrier.");
        }
    }

    public static void WaitForPanelLoads(this Application app) => app.CommandContext.WaitForPanelLoads();

    public static void RunWithPanelLoadBarriers(this Application app)
    {
        void Barrier() => app.WaitForPanelLoads();
        app.ApplicationInputRequested += Barrier;
        try { app.Run(); }
        finally { app.ApplicationInputRequested -= Barrier; }
    }
}
