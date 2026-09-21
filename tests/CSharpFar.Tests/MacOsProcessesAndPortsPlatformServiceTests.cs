using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Platform.MacOs;

namespace CSharpFar.Tests;

public sealed class MacOsProcessesAndPortsPlatformServiceTests
{
    [Fact]
    public void Native_layout_matches_xnu_contract()
    {
        Assert.Equal(8, MacOsProcessesAndPortsParser.ProcFdInfoSize);
        Assert.Equal(136, MacOsProcessesAndPortsParser.ProcBsdInfoSize);
        Assert.Equal(792, MacOsProcessesAndPortsParser.SocketFdInfoSize);
        Assert.Equal(120, MacOsProcessesAndPortsParser.BsdStartSecondsOffset);
        Assert.Equal(128, MacOsProcessesAndPortsParser.BsdStartMicrosecondsOffset);
        Assert.Equal(176, MacOsProcessesAndPortsParser.SocketTypeOffset);
        Assert.Equal(180, MacOsProcessesAndPortsParser.SocketProtocolOffset);
        Assert.Equal(184, MacOsProcessesAndPortsParser.SocketFamilyOffset);
        Assert.Equal(256, MacOsProcessesAndPortsParser.SocketKindOffset);
        Assert.Equal(264, MacOsProcessesAndPortsParser.ProtocolInfoOffset);
        Assert.Equal(268, MacOsProcessesAndPortsParser.InLocalPortOffset);
        Assert.Equal(288, MacOsProcessesAndPortsParser.InVFlagOffset);
        Assert.Equal(296, MacOsProcessesAndPortsParser.InForeignAddressOffset);
        Assert.Equal(312, MacOsProcessesAndPortsParser.InLocalAddressOffset);
        Assert.Equal(340, MacOsProcessesAndPortsParser.In6InterfaceIndexOffset);
        Assert.Equal(344, MacOsProcessesAndPortsParser.TcpStateOffset);
    }

    [Fact]
    public void Parser_handles_dual_stack_ipv4_and_network_byte_order()
    {
        byte[] socket = SocketInfo(
            family: MacOsProcessesAndPortsParser.AfInet6,
            type: MacOsProcessesAndPortsParser.SockStream,
            protocol: MacOsProcessesAndPortsParser.IpProtoTcp,
            kind: MacOsProcessesAndPortsParser.SockInfoTcp,
            vflag: MacOsProcessesAndPortsParser.IniIpv4,
            localAddress: IPAddress.Parse("127.0.0.1"),
            localPort: 5000,
            remoteAddress: IPAddress.Parse("10.20.30.40"),
            remotePort: 443,
            tcpState: 4);

        MacOsProcessesAndPortsParser.RawSocketEndpoint endpoint =
            Assert.IsType<MacOsProcessesAndPortsParser.RawSocketEndpoint>(
                MacOsProcessesAndPortsParser.ParseSocket(socket, 42));

        Assert.Equal(IPAddress.Parse("127.0.0.1"), endpoint.LocalAddress);
        Assert.Equal(5000, endpoint.LocalPort);
        Assert.Equal(IPAddress.Parse("10.20.30.40"), endpoint.RemoteAddress);
        Assert.Equal(443, endpoint.RemotePort);
        Assert.Equal(TcpState.Established, endpoint.State);
    }

    [Fact]
    public void Parser_preserves_ipv6_scope_and_normalizes_listener_remote_endpoint()
    {
        byte[] socket = SocketInfo(
            family: MacOsProcessesAndPortsParser.AfInet6,
            type: MacOsProcessesAndPortsParser.SockStream,
            protocol: MacOsProcessesAndPortsParser.IpProtoTcp,
            kind: MacOsProcessesAndPortsParser.SockInfoTcp,
            vflag: MacOsProcessesAndPortsParser.IniIpv6,
            localAddress: IPAddress.Parse("fe80::1234"),
            localPort: 8080,
            remoteAddress: IPAddress.Parse("fe80::5678"),
            remotePort: 8443,
            tcpState: 1,
            interfaceIndex: 17);

        MacOsProcessesAndPortsParser.RawSocketEndpoint endpoint =
            Assert.IsType<MacOsProcessesAndPortsParser.RawSocketEndpoint>(
                MacOsProcessesAndPortsParser.ParseSocket(socket, 42));

        Assert.Equal(17, endpoint.LocalAddress.ScopeId);
        Assert.Equal(TcpState.Listen, endpoint.State);
        Assert.Null(endpoint.RemoteAddress);
        Assert.Null(endpoint.RemotePort);
    }

    [Fact]
    public void Parser_normalizes_udp_and_unknown_tcp_state()
    {
        byte[] udp = SocketInfo(
            MacOsProcessesAndPortsParser.AfInet,
            MacOsProcessesAndPortsParser.SockDgram,
            MacOsProcessesAndPortsParser.IpProtoUdp,
            MacOsProcessesAndPortsParser.SockInfoIn,
            MacOsProcessesAndPortsParser.IniIpv4,
            IPAddress.Any,
            53,
            IPAddress.Parse("8.8.8.8"),
            9999,
            0);
        MacOsProcessesAndPortsParser.RawSocketEndpoint udpEndpoint =
            Assert.IsType<MacOsProcessesAndPortsParser.RawSocketEndpoint>(
                MacOsProcessesAndPortsParser.ParseSocket(udp, 7));
        Assert.Equal(53, udpEndpoint.LocalPort);
        Assert.Null(udpEndpoint.RemoteAddress);
        Assert.Null(udpEndpoint.RemotePort);
        Assert.Null(udpEndpoint.State);

        Assert.Equal(TcpState.CloseWait, MacOsProcessesAndPortsParser.MapTcpState(5));
        Assert.Equal(TcpState.Unknown, MacOsProcessesAndPortsParser.MapTcpState(12345));
    }

    [Fact]
    public void Process_metadata_uses_name_fallback_and_microsecond_precision()
    {
        byte[] bytes = new byte[MacOsProcessesAndPortsParser.ProcBsdInfoSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(MacOsProcessesAndPortsParser.BsdPidOffset), 77);
        "worker"u8.CopyTo(bytes.AsSpan(MacOsProcessesAndPortsParser.BsdCommOffset));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(MacOsProcessesAndPortsParser.BsdStartSecondsOffset), 1_700_000_000);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(MacOsProcessesAndPortsParser.BsdStartMicrosecondsOffset), 123_456);

        MacOsProcessesAndPortsParser.ParsedProcessInfo info =
            MacOsProcessesAndPortsParser.ParseBsdInfo(bytes, 77);

        Assert.Equal("worker", info.Name);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddTicks(1_234_560), info.StartedAt);
        Assert.Null(MacOsProcessesAndPortsParser.NormalizeProcessStartTime(1_700_000_000, 1_000_000));
        Assert.Null(MacOsProcessesAndPortsParser.NormalizeProcessStartTime(ulong.MaxValue, 0));
    }

    [Fact]
    public void Positive_partial_fixed_socket_structure_is_capture_failure()
    {
        var native = new PartialSocketNative();
        var service = new MacOsProcessesAndPortsPlatformService(native);

        Assert.Throws<InvalidDataException>(() => service.CaptureSnapshot(new ProcessesAndPortsQuery()));
    }

    [Fact]
    public void MacOs_loopback_capture_finds_own_tcp_udp_ipv4_ipv6_and_established_connection()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        using var listener4 = new TcpListener(IPAddress.Loopback, 0);
        listener4.Start();
        int tcp4Port = ((IPEndPoint)listener4.LocalEndpoint).Port;

        using var udp4 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udp4.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int udp4Port = ((IPEndPoint)udp4.LocalEndPoint!).Port;

        Assert.True(Socket.OSSupportsIPv6);
        using var listener6 = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener6.Server.DualMode = false;
        listener6.Start();
        int tcp6Port = ((IPEndPoint)listener6.LocalEndpoint).Port;

        using var establishedListener = new TcpListener(IPAddress.Loopback, 0);
        establishedListener.Start();
        int establishedPort = ((IPEndPoint)establishedListener.LocalEndpoint).Port;
        using var client = new TcpClient(AddressFamily.InterNetwork);
        client.Connect(IPAddress.Loopback, establishedPort);
        using TcpClient accepted = establishedListener.AcceptTcpClient();
        int clientPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

        var service = new MacOsProcessesAndPortsPlatformService();
        ProcessesAndPortsSnapshot all = service.CaptureSnapshot(
            new ProcessesAndPortsQuery(IncludeTcpListeners: true, IncludeUdpEndpoints: true, IncludeOtherTcpConnections: true));
        ProcessNetworkEndpoint[] own = all.Endpoints.Where(x => x.Process.ProcessId == Environment.ProcessId).ToArray();

        Assert.Contains(own, x => x.Protocol == NetworkTransportProtocol.Tcp &&
            x.LocalPort == tcp4Port && x.LocalAddress.Equals(IPAddress.Loopback) &&
            x.TcpState == TcpState.Listen && x.RemoteAddress is null && x.RemotePort is null);
        Assert.Contains(own, x => x.Protocol == NetworkTransportProtocol.Udp &&
            x.LocalPort == udp4Port && x.LocalAddress.Equals(IPAddress.Loopback) &&
            x.TcpState is null && x.RemoteAddress is null && x.RemotePort is null);
        Assert.Contains(own, x => x.Protocol == NetworkTransportProtocol.Tcp &&
            x.LocalPort == tcp6Port && x.LocalAddress.Equals(IPAddress.IPv6Loopback) &&
            x.TcpState == TcpState.Listen);
        Assert.Contains(own, x => x.Protocol == NetworkTransportProtocol.Tcp &&
            x.LocalPort == clientPort && x.TcpState == TcpState.Established);

        ProcessesAndPortsSnapshot listenersOnly = service.CaptureSnapshot(
            new ProcessesAndPortsQuery(IncludeTcpListeners: true, IncludeUdpEndpoints: false, IncludeOtherTcpConnections: false));
        Assert.DoesNotContain(listenersOnly.Endpoints, x =>
            x.Process.ProcessId == Environment.ProcessId &&
            x.LocalPort == clientPort &&
            x.TcpState == TcpState.Established);
    }

    private static byte[] SocketInfo(
        int family,
        int type,
        int protocol,
        int kind,
        byte vflag,
        IPAddress localAddress,
        int localPort,
        IPAddress remoteAddress,
        int remotePort,
        int tcpState,
        ushort interfaceIndex = 0)
    {
        byte[] bytes = new byte[MacOsProcessesAndPortsParser.SocketFdInfoSize];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(MacOsProcessesAndPortsParser.SocketFamilyOffset), family);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(MacOsProcessesAndPortsParser.SocketTypeOffset), type);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(MacOsProcessesAndPortsParser.SocketProtocolOffset), protocol);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(MacOsProcessesAndPortsParser.SocketKindOffset), kind);
        bytes[MacOsProcessesAndPortsParser.InVFlagOffset] = vflag;
        WritePort(bytes, MacOsProcessesAndPortsParser.InLocalPortOffset, localPort);
        WritePort(bytes, MacOsProcessesAndPortsParser.InForeignPortOffset, remotePort);
        WriteAddress(bytes, MacOsProcessesAndPortsParser.InLocalAddressOffset, localAddress);
        WriteAddress(bytes, MacOsProcessesAndPortsParser.InForeignAddressOffset, remoteAddress);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(MacOsProcessesAndPortsParser.In6InterfaceIndexOffset), interfaceIndex);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(MacOsProcessesAndPortsParser.TcpStateOffset), tcpState);
        return bytes;
    }

    private static void WritePort(byte[] bytes, int offset, int port)
    {
        ushort native = BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReverseEndianness(checked((ushort)port))
            : checked((ushort)port);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), native);
    }

    private static void WriteAddress(byte[] bytes, int offset, IPAddress address)
    {
        byte[] addressBytes = address.GetAddressBytes();
        if (addressBytes.Length == 4)
            addressBytes.CopyTo(bytes, offset + 12);
        else
            addressBytes.CopyTo(bytes, offset);
    }

    private sealed class PartialSocketNative : IMacOsProcessesAndPortsNative
    {
        public MacOsNativeResult ListAllPids(IntPtr buffer, int bufferSize)
        {
            if (buffer == IntPtr.Zero)
                return new(1, 0);
            Marshal.WriteInt32(buffer, Environment.ProcessId);
            return new(1, 0);
        }

        public MacOsNativeResult PidInfo(int pid, int flavor, ulong arg, IntPtr buffer, int bufferSize)
        {
            if (flavor != MacOsProcessesAndPortsNative.ProcPidListFds)
                throw new InvalidOperationException();
            if (buffer == IntPtr.Zero)
                return new(MacOsProcessesAndPortsParser.ProcFdInfoSize, 0);
            Marshal.WriteInt32(buffer, 0, 10);
            Marshal.WriteInt32(buffer, 4, checked((int)MacOsProcessesAndPortsNative.ProxFdTypeSocket));
            return new(MacOsProcessesAndPortsParser.ProcFdInfoSize, 0);
        }

        public MacOsNativeResult PidFdInfo(int pid, int fd, int flavor, IntPtr buffer, int bufferSize) =>
            new(MacOsProcessesAndPortsParser.SocketFdInfoSize - 1, 0);

        public MacOsNativeResult PidPath(int pid, IntPtr buffer, int bufferSize) => throw new InvalidOperationException();
    }
}
