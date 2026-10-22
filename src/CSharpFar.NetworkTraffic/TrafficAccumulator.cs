namespace CSharpFar.NetworkTraffic;

internal sealed class TrafficAccumulator
{
    private sealed class Application(string key, string name, string? path)
    {
        public string Key = key, Name = name;
        public string? Path = path;
        public HashSet<string> Processes = [];
        public SortedSet<string> WindowTitles = new(StringComparer.OrdinalIgnoreCase);
        public long Received, Sent, Estimated;
        public Queue<(DateTimeOffset Time, long Received, long Sent)> Recent = [];
    }
    private readonly Dictionary<string, Application> applications = [];
    private readonly Queue<(DateTimeOffset Time, long Bytes)> unknownRecent = [];
    private readonly DateTimeOffset started;
    private long unknown, estimated, observed;
    private const int MaximumApplications = 2048;
    public TrafficAccumulator(DateTimeOffset started) => this.started = started;

    public void Add(NetworkByte data, string? key, string? name, string? path, string? process, bool estimate)
    {
        if (data.Bytes <= 0) return;
        observed += data.Bytes;
        if (key is null || (!applications.ContainsKey(key) && applications.Count >= MaximumApplications))
        { unknown += data.Bytes; unknownRecent.Enqueue((data.Time, data.Bytes)); return; }
        if (!applications.TryGetValue(key, out var app)) applications[key] = app = new(key, name ?? key, path);
        if (process is not null && app.Processes.Count < 128) app.Processes.Add(process);
        long receive = data.Direction == "Receive" ? data.Bytes : 0, send = data.Direction == "Send" ? data.Bytes : 0;
        app.Received += receive; app.Sent += send;
        if (estimate) { app.Estimated += data.Bytes; estimated += data.Bytes; }
        // Events are fed in event-time order. Coalesce into second buckets.
        var second = new DateTimeOffset(data.Time.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);
        app.Recent.Enqueue((second, receive, send));
    }
    public void UpdatePresentations(IEnumerable<ObservedProcessIdentity> processes)
    {
        foreach (var app in applications.Values) app.WindowTitles.Clear();
        foreach (var group in processes.GroupBy(p => p.ApplicationKey))
        {
            if (!applications.TryGetValue(group.Key, out var app)) continue;
            var named = group.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ApplicationName)) ?? group.First();
            app.Name = named.DisplayName;
            foreach (var process in group)
                if (!string.IsNullOrWhiteSpace(process.WindowTitle)) app.WindowTitles.Add(process.WindowTitle);
        }
    }
    public TrafficSnapshot Read(DateTimeOffset through, string status, bool degraded)
    {
        var cutoff = through - TimeSpan.FromSeconds(10);
        double seconds = Math.Clamp((through - started).TotalSeconds, 1, 10);
        foreach (var app in applications.Values)
        {
            while (app.Recent.TryPeek(out var e) && e.Time < cutoff) app.Recent.Dequeue();
            // Collapse events to a fixed maximum of eleven rolling second buckets.
            if (app.Recent.Count > 11)
            {
                var buckets = app.Recent.GroupBy(x => x.Time).Select(g => (g.Key, g.Sum(x => x.Received), g.Sum(x => x.Sent))).ToArray();
                app.Recent.Clear(); foreach (var b in buckets) app.Recent.Enqueue(b);
            }
        }
        while (unknownRecent.TryPeek(out var e) && e.Time < cutoff) unknownRecent.Dequeue();
        if (unknownRecent.Count > 11)
        {
            var buckets = unknownRecent.GroupBy(x => new DateTimeOffset(x.Time.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero)).Select(g => (g.Key, g.Sum(x => x.Bytes))).ToArray();
            unknownRecent.Clear(); foreach (var b in buckets) unknownRecent.Enqueue(b);
        }
        var rows = applications.Values.Select(a => new TrafficApplication(a.Key, a.Name, a.Path, a.Processes.Order().ToArray(),
            a.Recent.Sum(x => x.Received) / seconds, a.Recent.Sum(x => x.Sent) / seconds, a.Received, a.Sent, a.Estimated)
        { WindowTitles = a.WindowTitles.ToArray() })
            .OrderByDescending(x => x.ReceiveBytesPerSecond + x.SendBytesPerSecond).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(rows, unknown, unknownRecent.Sum(x => x.Bytes) / seconds, estimated, observed, through, status, degraded);
    }
}
