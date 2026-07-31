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
            Fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create()),
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

    [Fact]
    public void Table_layout_clips_long_process_names_without_moving_pid()
    {
        ProcessesAndPortsTableLayout layout = ProcessesAndPortsTableLayout.Calculate(90);
        ProcessesAndPortsRow shortName = Row("worker", IPAddress.Loopback);
        ProcessesAndPortsRow longName = Row("a-very-long-process-name-that-must-not-reach-the-pid", IPAddress.Loopback);

        string shortText = layout.FormatRow(shortName);
        string longText = layout.FormatRow(longName);
        CalculatedColumn pid = layout.Columns.Single(column => column.Definition.Header == "PID");

        Assert.Equal("  4242", CellSlice(shortText, pid));
        Assert.Equal("  4242", CellSlice(longText, pid));
        Assert.Contains('…', longText);
        Assert.Equal(layout.Width, ConsoleTextMetrics.GetCellWidth(longText));
    }

    [Fact]
    public void Table_layout_keeps_ipv6_state_and_remote_at_calculated_offsets()
    {
        ProcessesAndPortsTableLayout layout = ProcessesAndPortsTableLayout.Calculate(120);
        ProcessesAndPortsRow row = Row("worker", IPAddress.Parse("fe80::1234:5678:9abc:def0%17"));

        string text = layout.FormatRow(row);
        CalculatedColumn state = layout.Columns.Single(column => column.Definition.Header == "State");
        CalculatedColumn remote = layout.Columns.Single(column => column.Definition.Header == "Remote");

        Assert.StartsWith("Listen", CellSlice(text, state));
        Assert.StartsWith("[2001:db8", CellSlice(text, remote));
        Assert.Contains('…', text);
    }

    [Fact]
    public void Table_layout_uses_the_same_offsets_for_header_and_rows_at_narrow_width()
    {
        ProcessesAndPortsTableLayout layout = ProcessesAndPortsTableLayout.Calculate(35);
        string header = layout.FormatHeader();
        string row = layout.FormatRow(Row("界e\u0301-long-process", IPAddress.Loopback));

        Assert.Equal(layout.Width, ConsoleTextMetrics.GetCellWidth(header));
        Assert.Equal(layout.Width, ConsoleTextMetrics.GetCellWidth(row));
        foreach (CalculatedColumn column in layout.Columns)
        {
            string cell = CellSlice(header, column);
            Assert.Equal(column.Width, ConsoleTextMetrics.GetCellWidth(cell));
            Assert.Contains(ConsoleTextMetrics.TruncateToCells(column.Definition.Header, column.Width), cell);
        }
        Assert.Contains('…', row);
    }

    [Fact]
    public void Table_layout_reserves_a_stable_scrollbar_gutter_width()
    {
        ProcessesAndPortsTableLayout withoutScrollbar = ProcessesAndPortsTableLayout.Calculate(79);
        ProcessesAndPortsTableLayout withScrollbar = ProcessesAndPortsTableLayout.Calculate(79);

        Assert.Equal(withoutScrollbar.Width, withScrollbar.Width);
        Assert.Equal(withoutScrollbar.Columns.Select(column => column.Offset), withScrollbar.Columns.Select(column => column.Offset));
        Assert.Contains(withoutScrollbar.Columns, column => column.Definition.Header == "PID");
        Assert.Contains(withoutScrollbar.Columns, column => column.Definition.Header == "Proto");
        Assert.Contains(withoutScrollbar.Columns, column => column.Definition.Header == "Port");
    }

    private static ProcessesAndPortsRow Row(string name, IPAddress localAddress)
    {
        var process = new ProcessSnapshot(4242, name, null, DateTimeOffset.UtcNow);
        var endpoint = new ProcessNetworkEndpoint(
            NetworkTransportProtocol.Tcp,
            localAddress,
            443,
            IPAddress.Parse("2001:db8:1234:5678:9abc:def0:1234:5678"),
            8443,
            TcpState.Listen,
            process);
        return new ProcessesAndPortsRow(new(NetworkTransportProtocol.Tcp, localAddress, 443, endpoint.RemoteAddress, 8443, process.ProcessId), endpoint, name);
    }

    private static string CellSlice(string text, CalculatedColumn column)
    {
        int start = ConsoleTextMetrics.Utf16IndexFromCellOffset(text, column.Offset);
        return ConsoleTextMetrics.TruncateToCells(text[start..], column.Width);
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
