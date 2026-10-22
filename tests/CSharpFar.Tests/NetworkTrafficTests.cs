using CSharpFar.Module.Abstractions;
using CSharpFar.Module.NetworkTraffic;
using CSharpFar.NetworkTraffic;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;
namespace CSharpFar.Tests;

public sealed class NetworkTrafficTests
{
    [Fact]
    public void Ranking_aggregates_processes_and_decays_without_losing_session_or_unknown_bytes()
    {
        var t = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var totals = new TrafficAccumulator(t);
        NetworkByte Byte(int second, long bytes, string direction = "Receive") => new(t.AddSeconds(second), "TCP", "IPv4", direction, "127.0.0.1", 1, "127.0.0.1", 2, bytes);
        totals.Add(Byte(1, 10000), "large", "Large", "large.exe", "7 / epoch1", false);
        totals.Add(Byte(2, 2000), "large", "Large", "large.exe", "8 / epoch2", true);
        totals.Add(Byte(2, 1000, "Send"), "small", "Small", null, "9 / epoch3", false);
        totals.Add(Byte(2, 500), null, null, null, null, false);
        var first = totals.Read(t.AddSeconds(3), "live", false);
        Assert.Equal("large", first.Applications[0].Key);
        Assert.Equal(2, first.Applications[0].Processes.Count);
        Assert.Equal(13500, first.ObservedBytes);
        Assert.Equal(500, first.UnknownBytes);
        Assert.Equal(2000, first.EstimatedBytes);
        totals.Add(Byte(15, 2000), "small", "Small", null, "9 / epoch3", false);
        var next = totals.Read(t.AddSeconds(16), "live", false);
        Assert.Equal("small", next.Applications[0].Key);
        Assert.Equal(0, next.Applications.Single(x => x.Key == "large").ReceiveBytesPerSecond);
        Assert.Equal(0, next.UnknownBytesPerSecond);
        Assert.Equal(15500, next.ObservedBytes);
    }

    [Fact]
    public void Names_and_window_titles_refresh_without_splitting_the_application_or_changing_traffic()
    {
        var t = DateTimeOffset.UtcNow;
        var first = new ObservedProcessIdentity(7, t.UtcDateTime, "C:/Browser/browser.exe")
        { ApplicationName = "Friendly Browser", WindowTitle = "Downloads" };
        var second = new ObservedProcessIdentity(8, t.UtcDateTime, "C:/Browser/browser.exe")
        { WindowTitle = "News" };
        var totals = new TrafficAccumulator(t);
        var data = new NetworkByte(t, "TCP", "IPv4", "Receive", "127.0.0.1", 1234, "127.0.0.1", 5678, 1024);
        totals.Add(data, first.ApplicationKey, first.DisplayName, first.ExecutablePath, "7 / epoch", false);
        totals.Add(data, second.ApplicationKey, second.DisplayName, second.ExecutablePath, "8 / epoch", false);
        totals.UpdatePresentations([first, second]);
        var row = Assert.Single(totals.Read(t.AddSeconds(1), "live", false).Applications);
        Assert.Equal("Friendly Browser", row.Name);
        Assert.Equal(new[] { "Downloads", "News" }, row.WindowTitles);
        Assert.Equal(2048, row.ReceivedBytes);
        Assert.Equal(2, row.Processes.Count);
        Assert.Single(NetworkTrafficDialog.Filter([row], " news "));
        totals.UpdatePresentations([first with { WindowTitle = "Documentation" }]);
        var updated = Assert.Single(totals.Read(t.AddSeconds(2), "live", false).Applications);
        Assert.Equal(row.Key, updated.Key);
        Assert.Equal(row.ReceivedBytes, updated.ReceivedBytes);
        Assert.Equal(new[] { "Documentation" }, updated.WindowTitles);
        totals.UpdatePresentations([]);
        Assert.Empty(Assert.Single(totals.Read(t.AddSeconds(3), "live", false).Applications).WindowTitles);
        Assert.Equal("browser.exe", (first with { ApplicationName = null }).DisplayName);
    }

    [Fact]
    public void Windows_application_name_uses_the_executables_description_when_available()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var path = process.MainModule!.FileName!;
        var description = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription;
        var info = ProcessPresentationReader.Read(process, process.StartTime.ToUniversalTime());
        Assert.Equal(path, info.ExecutablePath);
        Assert.Equal(string.IsNullOrWhiteSpace(description) ? Path.GetFileName(path) : description.Trim(), info.DisplayName);
        Assert.Equal(process.Id, info.Pid);
    }

    [Fact]
    public void Application_limit_keeps_overflow_bytes_unknown()
    {
        var t = DateTimeOffset.UtcNow;
        var totals = new TrafficAccumulator(t);
        for (int i = 0; i < 2100; i++) totals.Add(new(t, "UDP", "IPv4", "Send", "0.0.0.0", 1, "127.0.0.1", 2, 10), $"{i}", $"{i}", null, $"{i}", false);
        var s = totals.Read(t.AddSeconds(1), "live", false);
        Assert.Equal(2048, s.Applications.Count);
        Assert.Equal(520, s.UnknownBytes);
        Assert.Equal(21000, s.ObservedBytes);
    }

    [Fact]
    public void Dialog_shows_traffic_and_closes_only_after_monitor_cleanup()
    {
        var driver = new FakeConsoleDriver(150, 32);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        var host = UiTestHost.Create(driver);
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        var ui = new ModuleUiServices { Screen = host.Screen, ModalDialogs = host.ModalDialogs, Palette = () => new ConsolePalette { Name = "Test" }, Fields = fields, Dialogs = new DialogService(host.ModalDialogs, fields) };
        var monitor = new TestMonitor();
        new NetworkTrafficDialog(ui).Show(monitor, CancellationToken.None);
        Assert.True(monitor.Stopped);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Receive/s"));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Send/s"));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Downloaded"));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Uploaded"));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Unattributed:"));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Browser"));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Window"));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Documentation"));
    }
    [Fact]
    public async Task Demo_menu_and_prefix_open_samples_without_constructing_a_real_monitor()
    {
        var driver = new FakeConsoleDriver(150, 32);
        var host = UiTestHost.Create(driver);
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        var ui = new ModuleUiServices { Screen = host.Screen, ModalDialogs = host.ModalDialogs, Palette = () => new ConsolePalette { Name = "Test" }, Fields = fields, Dialogs = new DialogService(host.ModalDialogs, fields) };
        await using var runtime = new CSharpFar.App.Plugins.NativePluginRuntime(
            new CSharpFar.App.Plugins.ApplicationPluginContext(CSharpFar.Plugin.Abstractions.PluginRunMode.Demo, () => CSharpFar.Core.Models.PanelSide.Left, () => "/"),
            _ => throw new InvalidOperationException("Real settings requested"),
            _ => new CSharpFar.App.Plugins.ApplicationPluginUi(ui),
            _ => throw new InvalidOperationException("Real credentials requested"));
        var registration = await runtime.RegisterAsync(new CSharpFar.Plugin.Abstractions.PluginDescriptor { Id = NetworkTrafficPlugin.Id, Title = "Network Traffic" },
            new NetworkTrafficPlugin(ui, () => throw new InvalidOperationException("Real collector requested in demo")));
        Assert.True(registration.Success, registration.Error);
        runtime.Seal();
        var menu = Assert.Single(runtime.MenuItems);
        var command = new CSharpFar.App.Plugins.PluginCommandAddress(menu.PluginId, menu.Item.CommandId);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        var fromMenu = await runtime.ExecuteAsync(command, CSharpFar.Plugin.Abstractions.PluginCommandOrigin.PluginsMenu, CSharpFar.Core.Models.PanelSide.Left);
        Assert.Equal(CSharpFar.Plugin.Abstractions.PluginCommandResultKind.Completed, fromMenu.Kind);
        Assert.True(runtime.TryResolvePrefix("TrAfFiC", out var prefix, out _));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        var fromPrefix = await runtime.ExecuteAsync(prefix, CSharpFar.Plugin.Abstractions.PluginCommandOrigin.CommandLine, CSharpFar.Core.Models.PanelSide.Right);
        Assert.Equal(CSharpFar.Plugin.Abstractions.PluginCommandResultKind.Completed, fromPrefix.Kind);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Browser"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Windows_collector_counts_live_loopback_or_explains_missing_privileges_and_stops_session(bool existingSockets, bool tcp)
    {
        if (!OperatingSystem.IsWindows()) return;
        var existing = Microsoft.Diagnostics.Tracing.Session.TraceEventSession.GetActiveSessionNames().ToHashSet();
        using var receiver = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        using var sender = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        using var client = new System.Net.Sockets.TcpClient();
        System.Net.Sockets.TcpClient? accepted = null;
        async Task ConnectTcp()
        {
            listener.Start();
            await client.ConnectAsync((System.Net.IPEndPoint)listener.LocalEndpoint);
            accepted = await listener.AcceptTcpClientAsync();
        }
        if (tcp && existingSockets) await ConnectTcp();
        var monitor = new WindowsNetworkTrafficMonitor();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var running = Task.Run(() => OperatingSystem.IsWindows() ? monitor.RunAsync(stop.Token) : Task.CompletedTask);
        try
        {
            await Task.Delay(700);
            if (running.IsCompleted)
            {
                Assert.Contains("Administrator access required", monitor.Snapshot.Status);
                Assert.True(monitor.Snapshot.IsDegraded);
                return;
            }
            if (tcp && !existingSockets) await ConnectTcp();
            if (!tcp && !existingSockets)
            {
                receiver.Client.Dispose(); sender.Client.Dispose();
                receiver.Client = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
                sender.Client = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
                receiver.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
                sender.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            }
            // Accepted sockets need a snapshot before traffic; packets before the first
            // sample legitimately remain Unknown, as explained by the live UI.
            await Task.Delay(300, stop.Token);
            var destination = (System.Net.IPEndPoint)receiver.Client.LocalEndPoint!;
            byte[] payload = new byte[8192];
            for (int i = 0; i < 128; i++)
            {
                if (tcp)
                {
                    await client.GetStream().WriteAsync(payload, stop.Token);
                    await accepted!.GetStream().ReadExactlyAsync(new byte[payload.Length], stop.Token);
                }
                else
                {
                    await sender.SendAsync(payload, destination, stop.Token);
                    await receiver.ReceiveAsync(stop.Token);
                }
                await Task.Delay(4, stop.Token);
            }
            await Task.Delay(8500, stop.Token);
            var s = monitor.Snapshot;
            Assert.False(s.IsDegraded, s.Status);
            Assert.True(s.ObservedBytes >= 2 * 1048576, $"Observed {s.ObservedBytes}: {s.Status}");
            var own = s.Applications.Where(a => a.Processes.Any(p => p.StartsWith($"{Environment.ProcessId} /"))).ToArray();
            Assert.NotEmpty(own);
            Assert.True(own.Sum(a => a.ReceivedBytes + a.SentBytes) >= 2 * 1048576, $"Attributed {own.Sum(a => a.ReceivedBytes + a.SentBytes)}, Unknown {s.UnknownBytes}");
        }
        finally
        {
            stop.Cancel(); await running; accepted?.Dispose();
            Assert.DoesNotContain(Microsoft.Diagnostics.Tracing.Session.TraceEventSession.GetActiveSessionNames(),
                name => name.StartsWith("CSharpFar.NetworkTraffic.") && !existing.Contains(name));
        }
    }

    private sealed class TestMonitor : INetworkTrafficMonitor
    {
        public bool Stopped;
        public TrafficSnapshot Snapshot { get; } = new([new("b", "Browser", "browser.exe", ["7"], 1024, 2048, 10240, 20480, 0) { WindowTitles = ["Documentation - Browser"] }], 0, 0, 0, 30720, null, "Live");
        public async Task RunAsync(CancellationToken token)
        { try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { } finally { Stopped = true; } }
    }
}
