using System.Net;
namespace CSharpFar.NetworkTraffic;

internal static class TrafficOwnership
{
    public static (NetworkByte Byte, ByteOwnerDecision Owner) Resolve(NetworkByte data,
        AfdOwnershipTimeline history, IReadOnlyList<EndpointEpochSnapshot> snapshots, bool reliable)
    {
        if (!reliable) return (data, new(null, "CaptureLossOrLateEvent"));
        ByteOwnerDecision Find(NetworkByte value)
        {
            var owner = history.Resolve(value.Protocol, value.Family, value.LocalAddress, value.LocalPort, value.RemoteAddress, value.RemotePort, value.Time);
            return owner.Reason == "MissingLifecycle"
                ? EndpointEpochSampler.Resolve(snapshots, value.Protocol, value.Family, value.LocalAddress, value.LocalPort, value.RemoteAddress, value.RemotePort, value.Time)
                : owner;
        }
        var primary = Find(data);
        if (data.Protocol != "TCP" || data.Direction != "Receive" || IPAddress.IsLoopback(IPAddress.Parse(data.LocalAddress))) return (data, primary);
        // Manifest TCP Receive may describe the local-to-peer tuple. Require ownership
        // evidence on exactly one orientation; neither the header PID nor a guessed port wins.
        var reverse = data with { LocalAddress = data.RemoteAddress, LocalPort = data.RemotePort, RemoteAddress = data.LocalAddress, RemotePort = data.LocalPort };
        var alternate = Find(reverse);
        if (primary.Pid is not null && alternate.Pid is not null) return (data, new(null, "AmbiguousReceiveOrientation"));
        if (primary.Reason is "SharedEndpoint" or "LifecycleConflict" || alternate.Reason is "SharedEndpoint" or "LifecycleConflict")
            return (data, new(null, "AmbiguousReceiveOrientation"));
        return primary.Pid is null && alternate.Pid is not null ? (reverse, alternate) : (data, primary);
    }
}
