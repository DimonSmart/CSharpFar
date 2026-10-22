namespace CSharpFar.NetworkTraffic;

public sealed record TrafficApplication(string Key, string Name, string? ExecutablePath,
    IReadOnlyList<string> Processes, double ReceiveBytesPerSecond, double SendBytesPerSecond,
    long ReceivedBytes, long SentBytes, long EstimatedBytes)
{
    public IReadOnlyList<string> WindowTitles { get; init; } = [];
}

public sealed record TrafficSnapshot(IReadOnlyList<TrafficApplication> Applications,
    long UnknownBytes, double UnknownBytesPerSecond, long EstimatedBytes, long ObservedBytes,
    DateTimeOffset? ThroughUtc, string Status, bool IsDegraded = false);

public interface INetworkTrafficMonitor
{
    TrafficSnapshot Snapshot { get; }
    Task RunAsync(CancellationToken cancellationToken);
}
