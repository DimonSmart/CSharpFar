using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using CSharpFar.Module.ProcessesAndPorts;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Platform.Windows;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ProcessesAndPortsTests
{
    [Fact]
    public void Projection_filters_sorts_and_uses_checkbox_denominator()
    {
        ProcessSnapshot first = new(42, "dotnet", "C:/dotnet.exe", DateTimeOffset.UtcNow);
        ProcessSnapshot second = new(7, "dns", "C:/dns.exe", DateTimeOffset.UtcNow);
        ProcessNetworkEndpoint[] endpoints =
        [
            new(NetworkTransportProtocol.Tcp, IPAddress.Loopback, 64341, IPAddress.Parse("20.42.65.90"), 443, TcpState.Established, first),
            new(NetworkTransportProtocol.Udp, IPAddress.Any, 53, null, null, null, second),
            new(NetworkTransportProtocol.Tcp, IPAddress.Loopback, 80, null, null, TcpState.Listen, first),
        ];

        IReadOnlyList<ProcessesAndPortsRow> visible = ProcessesAndPortsDialog.Project(endpoints, "64341", includeOtherTcp: true);

        ProcessesAndPortsRow row = Assert.Single(visible);
        Assert.Equal(64341, row.Endpoint.LocalPort);
        Assert.Equal("20.42.65.90", row.Endpoint.RemoteAddress!.ToString());
        Assert.Equal(2, endpoints.Count(x => x.Protocol == NetworkTransportProtocol.Tcp));
    }

    [Fact]
    public void Process_identity_is_unavailable_without_reliable_start_time()
    {
        Assert.Null(new ProcessSnapshot(12, "worker", null, null, ProcessMetadataStatus.Partial).Identity);
        Assert.NotNull(new ProcessSnapshot(12, "worker", null, DateTimeOffset.UtcNow).Identity);
    }

    [Fact]
    public void Tcp6_parser_preserves_scope_and_omits_listener_remote_endpoint()
    {
        byte[] row = new byte[56];
        IPAddress.Parse("fe80::1234").GetAddressBytes().CopyTo(row, 0);
        BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(16), 17);
        BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(20), 5000);
        IPAddress.Parse("fe80::5678").GetAddressBytes().CopyTo(row, 24);
        BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(40), 18);
        BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(44), 443);
        BinaryPrimitives.WriteInt32LittleEndian(row.AsSpan(48), (int)TcpState.Listen);
        BinaryPrimitives.WriteInt32LittleEndian(row.AsSpan(52), 99);

        WindowsProcessesAndPortsPlatformService.RawEndpoint endpoint = WindowsProcessesAndPortsPlatformService.ParseTcp6Row(row);

        Assert.Equal(17, endpoint.LocalAddress.ScopeId);
        Assert.Null(endpoint.RemoteAddress);
        Assert.Null(endpoint.RemotePort);
        Assert.Equal(5000, endpoint.LocalPort);
        Assert.Equal(99, endpoint.ProcessId);
    }

    [Fact]
    public void Truncated_native_row_raises_controlled_error()
    {
        Assert.Throws<InvalidDataException>(() => WindowsProcessesAndPortsPlatformService.ParseUdp6Row(new byte[27]));
    }

    [Fact]
    public void Dialog_remains_open_when_filter_hides_all_endpoints()
    {
        var driver = new FakeConsoleDriver(100, 30);
        driver.EnqueueKey(new ConsoleKeyInfo('z', ConsoleKey.Z, false, false, false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        UiTestHost host = UiTestHost.Create(driver);
        var ui = new ModuleUiServices
        {
            Screen = host.Screen,
            ModalDialogs = host.ModalDialogs,
            Palette = () => new ConsolePalette { Name = "Test" },
            TextFieldHistory = new SingleLineTextHistoryRegistry(),
        };
        var endpoint = new ProcessNetworkEndpoint(
            NetworkTransportProtocol.Tcp,
            IPAddress.Loopback,
            8080,
            null,
            null,
            TcpState.Listen,
            new ProcessSnapshot(42, "worker", null, DateTimeOffset.UtcNow));
        var platform = new FakeProcessesAndPortsPlatformService([endpoint]);

        new ProcessesAndPortsDialog(ui, platform).Show(null);

        Assert.Equal(1, platform.CaptureCount);
    }

    private sealed class FakeProcessesAndPortsPlatformService(IReadOnlyList<ProcessNetworkEndpoint> endpoints) : IProcessesAndPortsPlatformService
    {
        public ProcessesAndPortsSupportInfo Support { get; } = new(true, false);
        public int CaptureCount { get; private set; }

        public ProcessesAndPortsSnapshot CaptureSnapshot(ProcessesAndPortsQuery query, CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            return new ProcessesAndPortsSnapshot(DateTimeOffset.UtcNow, endpoints);
        }

        public ProcessTerminationResult TerminateProcess(ProcessIdentity identity, CancellationToken cancellationToken = default) =>
            new(ProcessTerminationStatus.NotSupported);
    }
}
