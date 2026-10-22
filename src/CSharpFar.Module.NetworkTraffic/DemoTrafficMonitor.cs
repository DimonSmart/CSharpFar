using CSharpFar.NetworkTraffic;
namespace CSharpFar.Module.NetworkTraffic;

internal sealed class DemoTrafficMonitor : INetworkTrafficMonitor
{
    public TrafficSnapshot Snapshot { get; } = new([
        new("browser","Browser","demo/Browser",["101 / sample"],5242880,65536,52428800,655360,0) { WindowTitles = ["Downloads - Browser"] },
        new("sync","Cloud Sync","demo/Cloud Sync",["102 / sample"],131072,1048576,1310720,10485760,1048576),
        new("chat","Chat","demo/Chat",["103 / sample"],8192,4096,81920,40960,0) { WindowTitles = ["Team chat"] }],
        16384, 1024, 1048576, 65019904, null, "Demo sample / no live collection");
    public async Task RunAsync(CancellationToken token)
    { try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { } }
}
