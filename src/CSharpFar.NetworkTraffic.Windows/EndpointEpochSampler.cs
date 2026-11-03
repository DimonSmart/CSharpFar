using System.Diagnostics;
namespace CSharpFar.NetworkTraffic;

internal sealed record EndpointEpochRow(IpHelperEndpoint Endpoint, DateTime ProcessCreationUtc);
internal sealed record ObservedProcessIdentity(int Pid, DateTime CreatedUtc, string? ExecutablePath)
{
    public string? ApplicationName { get; init; }
    public string? WindowTitle { get; init; }
    public string? ProcessName { get; init; }
    public string ApplicationKey => ExecutablePath?.ToUpperInvariant() ?? $"pid:{Pid}:{CreatedUtc.Ticks}";
    public string DisplayName => ApplicationName ?? (ExecutablePath is string path ? Path.GetFileName(path) : ProcessName ?? $"PID {Pid}");
}
internal sealed record EndpointEpochSnapshot(DateTimeOffset TakenUtc, EndpointEpochRow[] Rows)
{
    public ObservedProcessIdentity[] Processes { get; init; } = [];
}
internal sealed class EndpointEpochSampler
{
    private readonly Dictionary<(int Pid, DateTime Created), (ObservedProcessIdentity Identity, DateTimeOffset Refreshed)> presentations = [];
    public EndpointEpochSnapshot Read()
    {
        var rows = IpHelperEndpointTable.ReadSnapshot();
        var epochs = new Dictionary<int, DateTime>();
        var identities = new List<ObservedProcessIdentity>();
        foreach (int pid in rows.Select(x => x.ProcessId).Where(x => x > 0).Distinct())
        {
            try
            {
                using var process = Process.GetProcessById(pid); var created = process.StartTime.ToUniversalTime(); epochs[pid] = created;
                var now = DateTimeOffset.UtcNow;
                if (!presentations.TryGetValue((pid, created), out var cached) || now - cached.Refreshed >= TimeSpan.FromSeconds(1))
                {
                    var identity = ProcessPresentationReader.Read(process, created, cached.Identity);
                    cached = (identity, now);
                    presentations[(pid, created)] = cached;
                }
                identities.Add(cached.Identity);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        foreach (var key in presentations.Keys.Where(k => !epochs.TryGetValue(k.Pid, out var created) || created != k.Created).ToArray()) presentations.Remove(key);
        return new(DateTimeOffset.UtcNow, rows.Where(x => epochs.ContainsKey(x.ProcessId)).Select(x => new EndpointEpochRow(x, epochs[x.ProcessId])).ToArray()) { Processes = identities.ToArray() };
    }
    public static ByteOwnerDecision Resolve(IReadOnlyList<EndpointEpochSnapshot> snapshots, string protocol, string family,
        string localAddress, int localPort, string remoteAddress, int remotePort, DateTimeOffset occurred)
    {
        var before = snapshots.LastOrDefault(x => x.TakenUtc <= occurred);
        var after = snapshots.FirstOrDefault(x => x.TakenUtc >= occurred);
        if (before is null || after is null || after.TakenUtc - before.TakenUtc > TimeSpan.FromMilliseconds(500)) return new(null, "NoBracketingSnapshot");
        EndpointEpochRow[] Matches(EndpointEpochSnapshot snapshot) => snapshot.Rows.Where(x => x.Endpoint.Protocol == protocol && x.Endpoint.Family == family &&
            x.Endpoint.LocalPort == localPort && (x.Endpoint.LocalAddress is "0.0.0.0" or "::" || x.Endpoint.LocalAddress == localAddress) &&
            (protocol == "UDP" || x.Endpoint.RemoteAddress == remoteAddress && x.Endpoint.RemotePort == remotePort)).ToArray();
        var a = Matches(before).Select(x => (x.Endpoint.ProcessId, x.ProcessCreationUtc)).Distinct().ToArray();
        var b = Matches(after).Select(x => (x.Endpoint.ProcessId, x.ProcessCreationUtc)).Distinct().ToArray();
        return a.Length == 1 && b.Length == 1 && a[0] == b[0] ? new(a[0].ProcessId, "BracketedSnapshotEstimate") : new(null, "SnapshotAmbiguityOrReuse");
    }
}
