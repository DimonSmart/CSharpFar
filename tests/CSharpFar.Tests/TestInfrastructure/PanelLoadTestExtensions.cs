using CSharpFar.App;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.State;

namespace CSharpFar.Tests;

/// <summary>Drive the real UI completion queue at an explicit test barrier.
/// Production input remains responsive while providers are running.</summary>
internal static class PanelLoadTestExtensions
{
    public static void WaitForPanelLoads(this ApplicationServices services) =>
        WaitForPanelLoads(services.Session.Panels, services.AutoRefresh, services.DrainUiActions);

    private static void WaitForPanelLoads(PanelSessionState panels, PanelAutoRefreshService autoRefresh, Action drain)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (panels.Left.IsLoading || panels.Right.IsLoading)
        {
            autoRefresh.ResetWaitToken();
            drain();
            if (!panels.Left.IsLoading && !panels.Right.IsLoading)
                break;
            int result = WaitHandle.WaitAny([autoRefresh.WaitToken.WaitHandle, timeout.Token.WaitHandle]);
            if (result == 1)
                throw new TimeoutException("Panel loading did not complete at the test barrier.");
        }
    }

    public static void WaitForPanelLoads(this Application app) => WaitForPanelLoads(app.Session.Panels, app.AutoRefresh, app.DrainUiActions);

    public static void RunWithPanelLoadBarriers(this Application app)
    {
        void Barrier() => app.WaitForPanelLoads();
        app.ApplicationInputRequested += Barrier;
        try { app.Run(); }
        finally { app.ApplicationInputRequested -= Barrier; }
    }
}
