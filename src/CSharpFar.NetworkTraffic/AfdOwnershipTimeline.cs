using System.Buffers.Binary;
using System.Net;
namespace CSharpFar.NetworkTraffic;

internal sealed class AfdEndpointLifetime
{
    public required string ProcessKey { get; init; }
    public required string EndpointKey { get; init; }
    public required int Pid { get; init; }
    public required string Protocol { get; init; }
    public required string Family { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public bool LifecycleConflict { get; set; }
    public DateTimeOffset? BoundUtc { get; set; }
    public DateTimeOffset? ClosedUtc { get; set; }
    public string? LocalAddress { get; set; }
    public int? LocalPort { get; set; }
    public string? RemoteAddress { get; set; }
    public int? RemotePort { get; set; }
}
internal sealed record ByteOwnerDecision(int? Pid, string Reason);
internal sealed class AfdOwnershipTimeline
{
    public List<AfdEndpointLifetime> Endpoints { get; } = [];
    private Dictionary<(string Protocol, string Family, int? Port), AfdEndpointLifetime[]> byLocalPort = [];
    private void Reindex() => byLocalPort = Endpoints.GroupBy(x => (x.Protocol, x.Family, x.LocalPort)).ToDictionary(g => g.Key, g => g.ToArray());
    private readonly Dictionary<(string Process, string Endpoint), AfdEndpointLifetime> active = [];
    public AfdOwnershipTimeline(IEnumerable<SocketSourceEvent> observations) => Add(observations);
    public void Add(IEnumerable<SocketSourceEvent> observations)
    {
        foreach (var observation in observations.OrderBy(x => x.OccurredUtc).ThenBy(x => x.Sequence))
        {
            if (observation.Provider != "Microsoft-Windows-Winsock-AFD") continue;
            var f = observation.Fields;
            if (!f.TryGetValue("Process", out var process) || !f.TryGetValue("Endpoint", out var endpoint) || process is null || endpoint is null) continue;
            var key = (process, endpoint);
            if (observation.Id == 1000 && f.GetValueOrDefault("EnterExit") == "0" && int.TryParse(f.GetValueOrDefault("ProcessId"), out int pid))
            {
                var lifetime = new AfdEndpointLifetime
                {
                    ProcessKey = process,
                    EndpointKey = endpoint,
                    Pid = pid,
                    Protocol = f.GetValueOrDefault("Protocol") == "6" ? "TCP" : f.GetValueOrDefault("Protocol") == "17" ? "UDP" : "Other",
                    Family = f.GetValueOrDefault("AddressFamily") == "2" ? "IPv4" : f.GetValueOrDefault("AddressFamily") == "23" ? "IPv6" : "Other",
                    CreatedUtc = observation.OccurredUtc
                };
                if (active.TryGetValue(key, out var previous) && previous.ClosedUtc is null)
                { previous.LifecycleConflict = true; lifetime.LifecycleConflict = true; }
                active[key] = lifetime; Endpoints.Add(lifetime);
            }
            if (!active.TryGetValue(key, out var current)) continue;
            if (f.GetValueOrDefault("EnterExit") != "1" || f.GetValueOrDefault("Status") != "0") continue;
            if (observation.Id == 1030 && DecodeSocketAddress(f.GetValueOrDefault("Address")) is { } local)
            {
                current.LocalAddress = local.Address; current.LocalPort = local.Port; current.BoundUtc = observation.OccurredUtc;
            }
            if (observation.Id is 1018 or 1021 && DecodeSocketAddress(f.GetValueOrDefault("Address")) is { } remote)
            { current.RemoteAddress = remote.Address; current.RemotePort = remote.Port; }
            if (observation.Id == 1001) current.ClosedUtc = observation.OccurredUtc;
        }
        Reindex();
    }
    public void Prune(DateTimeOffset cutoff)
    {
        Endpoints.RemoveAll(x => x.ClosedUtc < cutoff);
        foreach (var key in active.Where(x => x.Value.ClosedUtc < cutoff).Select(x => x.Key).ToArray()) active.Remove(key);
        Reindex();
    }
    public ByteOwnerDecision Resolve(string protocol, string family, string localAddress, int localPort,
        string remoteAddress, int remotePort, DateTimeOffset occurred)
    {
        var candidates = byLocalPort.GetValueOrDefault((protocol, family, localPort), []).Where(x =>
            x.BoundUtc <= occurred && (x.ClosedUtc is null || occurred <= x.ClosedUtc) &&
            (x.LocalAddress is "0.0.0.0" or "::" || x.LocalAddress == localAddress) &&
            (x.RemotePort is null || x.RemotePort == remotePort && x.RemoteAddress == remoteAddress)).ToArray();
        if (candidates.Any(x => x.LifecycleConflict)) return new(null, "LifecycleConflict");
        var pids = candidates.Select(x => (x.ProcessKey, x.Pid, x.CreatedUtc)).Distinct().ToArray();
        return pids.Length == 1 ? new(pids[0].Pid, "AfdLifetime") : new(null, pids.Length == 0 ? "MissingLifecycle" : "SharedEndpoint");
    }
    internal static (string Address, int Port)? DecodeSocketAddress(string? hex)
    {
        if (hex is null) return null;
        byte[] bytes;
        try { bytes = Convert.FromHexString(hex); } catch (FormatException) { return null; }
        if (bytes.Length < 8) return null;
        int af = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        int port = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2));
        return af switch
        {
            2 => (new IPAddress(bytes.AsSpan(4, 4)).ToString(), port),
            23 when bytes.Length >= 28 => (new IPAddress(bytes.AsSpan(8, 16), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24))).ToString(), port),
            _ => null,
        };
    }
}
