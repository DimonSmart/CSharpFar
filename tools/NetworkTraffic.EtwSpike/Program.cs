using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace CSharpFar.NetworkTraffic.EtwSpike;

/// <summary>
/// Diagnostic experiment, NOT a product collector. No ETL, no elevation, no session reuse.
/// Always emits a report; lack of verified observations must never be treated as a Go.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        string output = "etw-spike-result.json";
        if (args.Length != 0)
        {
            if (args.Length != 2 || args[0] != "--output" || string.IsNullOrWhiteSpace(args[1]))
            {
                Console.Error.WriteLine("Usage: NetworkTraffic.EtwSpike [--output report.json]");
                return 64;
            }

            output = args[1];
        }

        var report = new ProbeReport
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            SessionName = "CSharpFar.NetworkTraffic.Spike." + Guid.NewGuid().ToString("N"),
            OwnPid = Environment.ProcessId,
        };

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                report.Error = "WindowsRequired";
                return 2;
            }

            await CaptureAsync(report);
            return report.ObservedEvents > 0 && report.OwnPidEvents > 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            // Intentionally avoid sensitive diagnostic payload, addresses and stack traces.
            report.Error = exception.GetType().Name;
            report.ErrorDetail = exception.Message.Replace(report.SessionName, "[own-session]", StringComparison.OrdinalIgnoreCase);
            report.InnerError = exception.InnerException?.GetType().Name;
            report.InnerErrorCode = exception.InnerException?.HResult.ToString("X8", CultureInfo.InvariantCulture);
            report.ErrorCode = exception.HResult.ToString("X8", CultureInfo.InvariantCulture);
            return 2;
        }
        finally
        {
            report.FinishedAtUtc = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
    }

    private static async Task CaptureAsync(ProbeReport report)
    {
        // A fresh name is essential: never attach to, stop or reconfigure the kernel logger
        // or an existing trace session. StopOnDispose is a last-resort cleanup, not a
        // guarantee after process termination; orphan cleanup needs production design.
        report.Stage = "CreateTraceSession";
        using var session = new TraceEventSession(report.SessionName) { StopOnDispose = true };
        report.TraceSessionCreated = true;
        var source = session.Source;
        source.Kernel.All += data => Record(data, report);

        report.Stage = "EnableKernelProvider";
        session.EnableKernelProvider(
            KernelTraceEventParser.Keywords.NetworkTCPIP |
            KernelTraceEventParser.Keywords.Process);

        report.SessionStarted = true;
        report.Stage = "Collect";
        var reading = Task.Run(() => source.Process());
        try
        {
            await Task.Delay(500);
            await AttemptAsync(report, "TCP/IPv4", () => TcpLoopbackAsync(IPAddress.Loopback));
            await AttemptAsync(report, "UDP/IPv4", () => UdpLoopbackAsync(IPAddress.Loopback));
            await AttemptAsync(report, "TCP/IPv6", () => TcpLoopbackAsync(IPAddress.IPv6Loopback));
            await AttemptAsync(report, "UDP/IPv6", () => UdpLoopbackAsync(IPAddress.IPv6Loopback));

            // Real-time ETW buffers can have significant delivery latency.
            await Task.Delay(4000);
        }
        finally
        {
            // Only this session instance is stopped (never NT Kernel Logger).
            session.Stop();
            await reading.WaitAsync(TimeSpan.FromSeconds(10));
            report.EventsLost = source.EventsLost;
            report.Stage = "Completed";
        }
    }

    private static void Record(TraceEvent data, ProbeReport report)
    {
        string task = data.TaskName ?? "";
        if (task is not ("TcpIp" or "UdpIp"))
            return;

        string opcode = data.OpcodeName ?? "";
        string direction = opcode.StartsWith("Send", StringComparison.OrdinalIgnoreCase)
            ? "Send"
            : opcode.StartsWith("Recv", StringComparison.OrdinalIgnoreCase)
                ? "Recv"
                : "Other";

        lock (report)
        {
            string key = task + "/" + opcode;
            if (!report.EventTypes.TryGetValue(key, out EventCounter? counter))
                report.EventTypes[key] = counter = new EventCounter();
            counter.Observed++;

            if (direction == "Other")
                return;

            // ProcessID in the ETW header can identify a different process.
            // The PID and size must come from this event's payload.
            long? pid = ReadPayloadInteger(data, "PID");
            long? size = ReadPayloadInteger(data, "size");
            if (pid is null or <= 0 || size is null or <= 0)
            {
                report.InvalidPayloadEvents++;
                return;
            }

            string addressText = ReadPayloadString(data, "saddr")
                ?? ReadPayloadString(data, "daddr")
                ?? "";
            string addressFamily = IPAddress.TryParse(addressText, out IPAddress? address)
                ? address.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4"
                : "Unavailable";

            counter.Valid++;
            counter.PayloadSizeTotal += size.Value;
            report.ObservedEvents++;
            if (pid == report.OwnPid)
                report.OwnPidEvents++;

            if (report.Samples.Count < 40)
            {
                report.Samples.Add(new EventSample
                {
                    Type = key,
                    PidFromPayload = pid.Value,
                    SizeFromPayload = size.Value,
                    Family = addressFamily,
                    // Wall-clock difference is a diagnostic approximation, not a rate clock.
                    DeliveryLagMilliseconds = Math.Max(0,
                        (DateTime.UtcNow - data.TimeStamp.ToUniversalTime()).TotalMilliseconds),
                });
            }
        }
    }

    private static long? ReadPayloadInteger(TraceEvent data, string name)
    {
        try
        {
            object? value = data.PayloadByName(name);
            return value is null ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException
                                           or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static string? ReadPayloadString(TraceEvent data, string name)
    {
        try { return data.PayloadByName(name)?.ToString(); }
        catch (ArgumentException) { return null; }
    }

    private static async Task AttemptAsync(ProbeReport report, string type, Func<Task> attempt)
    {
        try
        {
            await attempt();
            report.GeneratedTraffic[type] = "Completed";
        }
        catch (Exception exception) when (exception is SocketException or IOException or NotSupportedException)
        {
            report.GeneratedTraffic[type] = exception.GetType().Name;
        }
    }

    private static async Task TcpLoopbackAsync(IPAddress ip)
    {
        var listener = new TcpListener(ip, 0);
        listener.Start();
        try
        {
            using var client = new TcpClient(ip.AddressFamily);
            Task connect = client.ConnectAsync(ip, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var peer = await listener.AcceptTcpClientAsync();
            await connect;

            byte[] payload = new byte[8192];
            Task receiver = Task.Run(async () =>
            {
                byte[] buffer = new byte[8192];
                Stream input = peer.GetStream();
                while (await input.ReadAsync(buffer) != 0) { }
            });

            Stream output = client.GetStream();
            for (int i = 0; i < 64; i++)
                await output.WriteAsync(payload);
            client.Client.Shutdown(SocketShutdown.Send);
            await receiver;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task UdpLoopbackAsync(IPAddress ip)
    {
        using var receiver = new UdpClient(new IPEndPoint(ip, 0));
        using var sender = new UdpClient(ip.AddressFamily);
        byte[] payload = new byte[1024];
        var destination = (IPEndPoint)receiver.Client.LocalEndPoint!;
        for (int i = 0; i < 64; i++)
        {
            await sender.SendAsync(payload, destination);
            await receiver.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ProbeReport
    {
        public DateTimeOffset CapturedAtUtc { get; set; }
        public DateTimeOffset FinishedAtUtc { get; set; }
        public string Os { get; set; } = "";
        public string SessionName { get; set; } = "";
        public int OwnPid { get; set; }
        public bool TraceSessionCreated { get; set; }
        public bool SessionStarted { get; set; }
        public string Stage { get; set; } = "NotStarted";
        public string? ErrorDetail { get; set; }
        public string? InnerError { get; set; }
        public string? InnerErrorCode { get; set; }
        public string? Error { get; set; }
        public string? ErrorCode { get; set; }
        public long ObservedEvents { get; set; }
        public long OwnPidEvents { get; set; }
        public long InvalidPayloadEvents { get; set; }
        public int EventsLost { get; set; }
        public Dictionary<string, string> GeneratedTraffic { get; } = [];
        public Dictionary<string, EventCounter> EventTypes { get; } = [];
        public List<EventSample> Samples { get; } = [];
    }

    private sealed class EventCounter
    {
        public long Observed { get; set; }
        public long Valid { get; set; }
        public long PayloadSizeTotal { get; set; }
    }

    private sealed class EventSample
    {
        public string Type { get; set; } = "";
        public long PidFromPayload { get; set; }
        public long SizeFromPayload { get; set; }
        public string Family { get; set; } = "";
        public double DeliveryLagMilliseconds { get; set; }
    }
}
