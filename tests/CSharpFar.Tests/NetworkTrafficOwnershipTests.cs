using CSharpFar.NetworkTraffic;
namespace CSharpFar.Tests;

public sealed class NetworkTrafficOwnershipTests
{
    [Fact]
    public void Ownership_handles_lifecycle_ambiguity_reuse_and_snapshot_epochs()
    {
        var t = DateTimeOffset.Parse("2026-01-01T00:00:00Z"); int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        SocketSourceEvent Event(int id, int ms, Dictionary<string, string?> f) => new(ms, "Microsoft-Windows-Winsock-AFD", id, t.AddMilliseconds(ms), f);
        Dictionary<string, string?> Fields(string phase = "1", string endpoint = "a", string process = "p") => new() { ["Process"] = process, ["Endpoint"] = endpoint, ["EnterExit"] = phase, ["Status"] = "0" };
        var create = Fields("0"); create["ProcessId"] = "7"; create["AddressFamily"] = "2"; create["Protocol"] = "17";
        var bind = Fields(); bind["Address"] = "02003039000000000000000000000000";
        var records = new[] { Event(1000, 0, create), Event(1030, 10, bind), Event(1001, 100, Fields()) };
        var timeline = new AfdOwnershipTimeline(records);
        ByteOwnerDecision Resolve(AfdOwnershipTimeline x, int ms) => x.Resolve("UDP", "IPv4", "127.0.0.1", 12345, "127.0.0.1", 5555, t.AddMilliseconds(ms));
        Check(Resolve(timeline, 50).Pid == 7, "opaque endpoint + payload PID maps bytes");
        Check(Resolve(timeline, 5).Pid is null, "before bind"); Check(Resolve(timeline, 101).Pid is null, "after close");
        Check(Resolve(new(records.Reverse()), 50) == Resolve(timeline, 50), "late/reordered ownership");
        var anotherCreate = new Dictionary<string, string?>(create) { ["Endpoint"] = "b", ["Process"] = "q", ["ProcessId"] = "8" };
        var anotherBind = new Dictionary<string, string?>(bind) { ["Endpoint"] = "b", ["Process"] = "q" };
        var shared = new AfdOwnershipTimeline(records.Concat(new[] { Event(1000, 20, anotherCreate), Event(1030, 30, anotherBind) }));
        Check(Resolve(shared, 50).Pid is null, "shared UDP tuple remains Unknown");
        var reused = new Dictionary<string, string?>(create) { ["ProcessId"] = "8" };
        var reuse = new AfdOwnershipTimeline(records.Concat(new[] { Event(1000, 110, reused), Event(1030, 120, bind) }));
        Check(Resolve(reuse, 50).Pid == 7 && Resolve(reuse, 150).Pid == 8, "endpoint pointer reuse has separate generations");
        var missingClose = new AfdOwnershipTimeline(records.Take(2).Concat(new[] { Event(1000, 110, reused), Event(1030, 120, bind), Event(1001, 160, Fields()) }));
        Check(Resolve(missingClose, 150).Pid is null && Resolve(missingClose, 170).Pid is null, "missing closure never fabricates a boundary");
        Check(AfdOwnershipTimeline.DecodeSocketAddress("17003039000000000000000000000000000000000000000100000000")?.Address == "::1", "IPv6 sockaddr layout");
        EndpointEpochSnapshot Snapshot(int ms, int pid, DateTime created) => new(t.AddMilliseconds(ms), [new(new("TCP", "IPv4", "127.0.0.1", 12345, "127.0.0.1", 5555, "5", pid), created)]);
        ByteOwnerDecision Estimate(EndpointEpochSnapshot[] samples) => EndpointEpochSampler.Resolve(samples, "TCP", "IPv4", "127.0.0.1", 12345, "127.0.0.1", 5555, t.AddMilliseconds(50));
        var epoch = t.UtcDateTime.AddSeconds(-10);
        Check(Estimate([Snapshot(0, 7, epoch), Snapshot(100, 7, epoch)]).Reason == "BracketedSnapshotEstimate", "snapshot inference explicitly labelled");
        Check(Estimate([Snapshot(0, 7, epoch), Snapshot(100, 7, epoch.AddSeconds(1))]).Pid is null, "snapshot PID reuse");
        Check(Estimate([Snapshot(0, 7, epoch), Snapshot(600, 7, epoch)]).Pid is null, "stale snapshot gap");
        Check(Estimate([Snapshot(0, 7, epoch)]).Pid is null, "no extrapolation beyond snapshot");
        var bytes = new NetworkByte(t.AddMilliseconds(50), "UDP", "IPv4", "Send", "127.0.0.1", 12345, "127.0.0.1", 5555, 1024);
        Check(TrafficOwnership.Resolve(bytes, timeline, [], true).Owner.Pid == 7, "production resolver uses lifecycle evidence");
        var lost = TrafficOwnership.Resolve(bytes, timeline, [], false);
        Check(lost.Owner.Pid is null && lost.Byte.Bytes == 1024, "loss or late event preserves Unknown byte volume");
        var externalSamples = new[] { Snapshot(0, 7, epoch), Snapshot(100, 7, epoch) }.Select(sample => sample with
        { Rows = [new(new("TCP", "IPv4", "10.0.0.1", 12345, "203.0.113.9", 443, "5", 7), epoch)] }).ToArray();
        var receive = bytes with { Protocol = "TCP", Direction = "Receive", LocalAddress = "203.0.113.9", LocalPort = 443, RemoteAddress = "10.0.0.1", RemotePort = 12345 };
        var external = TrafficOwnership.Resolve(receive, new([]), externalSamples, true);
        Check(external.Owner.Pid == 7 && external.Owner.Reason == "BracketedSnapshotEstimate" && external.Byte.LocalAddress == "10.0.0.1", "external TCP receive orientation needs full tuple evidence");
        Assert.Equal(15, count);
    }
}
