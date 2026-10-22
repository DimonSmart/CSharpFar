using CSharpFar.Module.Abstractions;
using CSharpFar.NetworkTraffic;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Module.NetworkTraffic;

public sealed class NetworkTrafficPlugin : ICSharpFarPlugin
{
    private readonly ModuleUiServices ui;
    private readonly Func<INetworkTrafficMonitor> createMonitor;
    public NetworkTrafficPlugin(ModuleUiServices ui, Func<INetworkTrafficMonitor> createMonitor)
    {
        this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
        this.createMonitor = createMonitor ?? throw new ArgumentNullException(nameof(createMonitor));
    }
    public const string Id = "network-traffic";
    public ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        host.RegisterCommand(new PluginCommandDescriptor
        {
            Id = "open",
            Title = "Network Traffic",
            ExecuteAsync = (context, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (context.Application.RunMode != PluginRunMode.Demo && !OperatingSystem.IsWindows())
                    context.Ui.ShowMessage("Network Traffic", "Live traffic monitoring is currently available on Windows only.");
                else new NetworkTrafficDialog(ui).Show(context.Application.RunMode == PluginRunMode.Demo ? new DemoTrafficMonitor() : createMonitor(), token);
                return ValueTask.FromResult(PluginCommandResult.Completed());
            },
        });
        host.RegisterMenuItem(new PluginMenuItemDescriptor { Id = "open", Title = "Network Traffic...", CommandId = "open", HotKey = 'N' });
        host.RegisterCommandPrefix("traffic", "open");
        return ValueTask.CompletedTask;
    }
}
