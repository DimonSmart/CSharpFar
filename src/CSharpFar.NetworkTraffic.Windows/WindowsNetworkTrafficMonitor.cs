using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace CSharpFar.NetworkTraffic;

[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkTrafficMonitor : INetworkTrafficMonitor
{
    private static readonly Guid Afd = new("e53c6823-7bb8-44bb-90dc-3f86090d48a6");
    private static readonly Guid Network = new("7dd42a49-5329-4832-8dfd-43d979153a88");
    private static readonly HashSet<string> Fields = new(StringComparer.OrdinalIgnoreCase)
    { "Process", "Endpoint", "ProcessId", "EnterExit", "Status", "AddressFamily", "Protocol", "Address", "size", "sport", "dport", "saddr", "daddr" };
    private TrafficSnapshot snapshot = new([], 0, 0, 0, 0, null, "Starting ETW collection...");
    public TrafficSnapshot Snapshot => Volatile.Read(ref snapshot);
    private int running;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("A monitor can run only once.");
        var gate = new object();
        var pending = new List<SocketSourceEvent>();
        var history = new AfdOwnershipTimeline([]);
        var snapshots = new List<EndpointEpochSnapshot>();
        var sampler = new EndpointEpochSampler();
        var start = DateTimeOffset.UtcNow;
        var totals = new TrafficAccumulator(start);
        long sequence = 0;
        string? error = null;
        bool degraded = false;
        DateTimeOffset finalized = start;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var session = new TraceEventSession("CSharpFar.NetworkTraffic." + Guid.NewGuid().ToString("N")) { StopOnDispose = true };
            var source = session.Source;
            source.Dynamic.All += data =>
            {
                if (data.ProviderGuid != Afd && data.ProviderGuid != Network) return;
                lock (gate)
                {
                    if (pending.Count >= 200000) { degraded = true; error = "Event backlog exceeded; attribution incomplete."; return; }
                    try
                    {
                        if (data.ProviderGuid == Afd && data.Version != 0) { degraded = true; error = "Unsupported AFD schema; attribution incomplete."; return; }
                        var fields = new Dictionary<string, string?>();
                        for (int i = 0; i < data.PayloadNames.Length; i++)
                        {
                            var field = data.PayloadNames[i]; if (!Fields.Contains(field)) continue;
                            object? value = data.PayloadValue(i);
                            fields[field] = value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(value, CultureInfo.InvariantCulture);
                        }
                        pending.Add(new(++sequence, data.ProviderName, (int)data.ID,
                            new(data.TimeStamp.ToUniversalTime()), fields));
                    }
                    catch (Exception ex) { degraded = true; error = "ETW decode failed: " + ex.Message; }
                }
            };
            session.EnableProvider(Afd, TraceEventLevel.Verbose, ulong.MaxValue, new TraceEventProviderOptions
            { EventIDsToEnable = new List<int> { 1000, 1001, 1018, 1021, 1030 } });
            session.EnableProvider(Network, TraceEventLevel.Verbose, ulong.MaxValue);
            var reading = Task.Run(source.Process, CancellationToken.None);
            try
            {
                var lastUpdate = DateTimeOffset.MinValue;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (reading.IsCompleted) { await reading; throw new InvalidOperationException("ETW collection ended unexpectedly."); }
                    snapshots.Add(sampler.Read());
                    var now = DateTimeOffset.UtcNow;
                    if (now - lastUpdate >= TimeSpan.FromSeconds(1))
                    {
                        lastUpdate = now;
                        var through = now - TimeSpan.FromSeconds(5);
                        List<SocketSourceEvent> batch;
                        bool bad;
                        string? message;
                        lock (gate)
                        {
                            if (source.EventsLost > 0) { degraded = true; error = "ETW events lost; attribution incomplete."; }
                            batch = pending.OrderBy(x => x.OccurredUtc).ThenBy(x => x.Sequence).ToList();
                            pending.Clear(); bad = degraded; message = error;
                        }
                        if (batch.Any(e => e.Provider == "Microsoft-Windows-Winsock-AFD" && e.OccurredUtc < finalized))
                        {
                            lock (gate) { degraded = true; error = "Late socket lifecycle events; attribution incomplete."; }
                            bad = true;
                        }
                        // Apply lifecycle before resolving bytes, including delayed close/create records.
                        history.Add(batch.Where(x => x.Provider == "Microsoft-Windows-Winsock-AFD"));
                        var processEpochs = snapshots.SelectMany(s => s.Processes.Select(p => (Identity: p, Time: s.TakenUtc)))
                            .GroupBy(p => p.Identity.Pid).ToDictionary(g => g.Key, g => g.GroupBy(p => p.Identity.CreatedUtc)
                                .Select(epoch => (Identity: epoch.Select(p => p.Identity).FirstOrDefault(p => p.ExecutablePath is not null) ?? epoch.First().Identity,
                                    First: epoch.Min(p => p.Time), Last: epoch.Max(p => p.Time))).ToArray());
                        foreach (var e in batch.Where(x => x.Provider == "Microsoft-Windows-Kernel-Network"))
                        {
                            if (e.OccurredUtc > through) { lock (gate) { if (pending.Count < 200000) pending.Add(e); else { degraded = true; error = "Event backlog exceeded; attribution incomplete."; } } continue; }
                            try
                            {
                                var data = NetworkByteDecoder.Decode(e); if (data is null) continue;
                                bool late = e.OccurredUtc < finalized;
                                var resolved = TrafficOwnership.Resolve(data, history, snapshots, !bad && !late);
                                data = resolved.Byte;
                                var owner = resolved.Owner;
                                var identity = owner.Pid is int pid && processEpochs.TryGetValue(pid, out var epochs)
                                    ? epochs.Where(p => p.First <= data.Time + TimeSpan.FromMilliseconds(500) && p.Last >= data.Time - TimeSpan.FromMilliseconds(500) && p.Identity.CreatedUtc <= data.Time.UtcDateTime).Select(p => p.Identity).ToArray()
                                    : [];
                                // A reliable process epoch is required even when a socket owner is known.
                                var process = identity.Length == 1 ? identity[0] : null;
                                string? key = process is null ? null : process.ApplicationKey;
                                totals.Add(data, key, process?.DisplayName,
                                    process?.ExecutablePath, process is null ? null : $"{process.Pid} / {process.CreatedUtc:O}", owner.Reason == "BracketedSnapshotEstimate");
                            }
                            catch (Exception ex) { lock (gate) { degraded = true; error = "Attribution decode failed: " + ex.Message; } }
                        }
                        finalized = through > start ? through : start;
                        history.Prune(now - TimeSpan.FromSeconds(20));
                        if (history.Endpoints.Count > 50000) { lock (gate) { degraded = true; error = "Socket history limit exceeded; attribution incomplete."; } history = new([]); }
                        snapshots.RemoveAll(s => s.TakenUtc < now - TimeSpan.FromSeconds(12));
                        lock (gate) { bad = degraded; message = error; }
                        totals.UpdatePresentations(snapshots[^1].Processes);
                        Volatile.Write(ref snapshot, totals.Read(finalized, message ?? (through <= start ? "Warming up (ETW delivery lag)..." : "Live / 10 s window / about 5 s delivery delay"), bad));
                    }
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                session.Stop();
                await reading.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            string message = ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005)
                ? "Administrator access required. Restart CSharpFar as administrator to monitor traffic."
                : "Traffic collection failed: " + ex.Message;
            Volatile.Write(ref snapshot, Snapshot with { Status = message, IsDegraded = true });
        }
    }
}
