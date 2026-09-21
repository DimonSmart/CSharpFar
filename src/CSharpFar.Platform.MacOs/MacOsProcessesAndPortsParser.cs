using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.Platform.MacOs;

internal static class MacOsProcessesAndPortsParser
{
    internal const int ProcFdInfoSize = 8;
    internal const int ProcBsdInfoSize = 136;
    internal const int ProcFileInfoSize = 24;
    internal const int SocketInfoSize = 768;
    internal const int SocketFdInfoSize = 792;

    internal const int ProcFdOffset = 0;
    internal const int ProcFdTypeOffset = 4;

    internal const int BsdPidOffset = 12;
    internal const int BsdCommOffset = 48;
    internal const int BsdCommLength = 16;
    internal const int BsdNameOffset = 64;
    internal const int BsdNameLength = 32;
    internal const int BsdStartSecondsOffset = 120;
    internal const int BsdStartMicrosecondsOffset = 128;

    private const int SocketInfoOffset = ProcFileInfoSize;
    internal const int SocketTypeOffset = SocketInfoOffset + 152;
    internal const int SocketProtocolOffset = SocketInfoOffset + 156;
    internal const int SocketFamilyOffset = SocketInfoOffset + 160;
    internal const int SocketKindOffset = SocketInfoOffset + 232;
    internal const int ProtocolInfoOffset = SocketInfoOffset + 240;

    internal const int InForeignPortOffset = ProtocolInfoOffset;
    internal const int InLocalPortOffset = ProtocolInfoOffset + 4;
    internal const int InVFlagOffset = ProtocolInfoOffset + 24;
    internal const int InForeignAddressOffset = ProtocolInfoOffset + 32;
    internal const int InLocalAddressOffset = ProtocolInfoOffset + 48;
    internal const int In6InterfaceIndexOffset = ProtocolInfoOffset + 76;
    internal const int TcpStateOffset = ProtocolInfoOffset + 80;

    internal const int AfInet = 2;
    internal const int AfInet6 = 30;
    internal const int SockStream = 1;
    internal const int SockDgram = 2;
    internal const int IpProtoTcp = 6;
    internal const int IpProtoUdp = 17;
    internal const int SockInfoIn = 1;
    internal const int SockInfoTcp = 2;
    internal const byte IniIpv4 = 0x1;
    internal const byte IniIpv6 = 0x2;

    internal static (int Fd, uint Type) ParseFd(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes, ProcFdInfoSize, "proc_fdinfo");
        return (ReadInt32(bytes, ProcFdOffset), ReadUInt32(bytes, ProcFdTypeOffset));
    }

    internal static ParsedProcessInfo ParseBsdInfo(ReadOnlySpan<byte> bytes, int expectedPid)
    {
        RequireExactLength(bytes, ProcBsdInfoSize, "proc_bsdinfo");
        int pid = checked((int)ReadUInt32(bytes, BsdPidOffset));
        if (pid <= 0 || pid != expectedPid)
            throw new InvalidDataException("macOS returned mismatched process metadata.");

        string? name = ReadCString(bytes.Slice(BsdNameOffset, BsdNameLength));
        name ??= ReadCString(bytes.Slice(BsdCommOffset, BsdCommLength));
        DateTimeOffset? startedAt = NormalizeProcessStartTime(
            ReadUInt64(bytes, BsdStartSecondsOffset),
            ReadUInt64(bytes, BsdStartMicrosecondsOffset));
        return new(pid, name, startedAt);
    }

    internal static RawSocketEndpoint? ParseSocket(ReadOnlySpan<byte> bytes, int processId)
    {
        RequireExactLength(bytes, SocketFdInfoSize, "socket_fdinfo");
        if (processId <= 0)
            return null;

        int family = ReadInt32(bytes, SocketFamilyOffset);
        if (family is not (AfInet or AfInet6))
            return null;

        int type = ReadInt32(bytes, SocketTypeOffset);
        int protocol = ReadInt32(bytes, SocketProtocolOffset);
        int kind = ReadInt32(bytes, SocketKindOffset);
        byte vflag = bytes[InVFlagOffset];
        if (vflag == 0 || (vflag & ~(IniIpv4 | IniIpv6)) != 0)
            return null;

        IPAddress? local = ParseAddress(bytes.Slice(InLocalAddressOffset, 16), vflag, ReadUInt16(bytes, In6InterfaceIndexOffset));
        if (local is null)
            return null;
        int localPort = ParsePort(ReadInt32(bytes, InLocalPortOffset));

        if (type == SockStream && protocol == IpProtoTcp && kind == SockInfoTcp)
        {
            TcpState state = MapTcpState(ReadInt32(bytes, TcpStateOffset));
            if (state == TcpState.Listen)
                return new(NetworkTransportProtocol.Tcp, local, localPort, null, null, state, processId);

            IPAddress? remote = ParseAddress(bytes.Slice(InForeignAddressOffset, 16), vflag, ReadUInt16(bytes, In6InterfaceIndexOffset));
            int? remotePort = remote is null ? null : ParsePort(ReadInt32(bytes, InForeignPortOffset));
            return new(NetworkTransportProtocol.Tcp, local, localPort, remote, remotePort, state, processId);
        }

        if (type == SockDgram && protocol == IpProtoUdp && kind == SockInfoIn)
            return new(NetworkTransportProtocol.Udp, local, localPort, null, null, null, processId);

        return null;
    }

    internal static DateTimeOffset? NormalizeProcessStartTime(ulong seconds, ulong microseconds)
    {
        if (microseconds >= 1_000_000 || seconds > long.MaxValue)
            return null;
        try
        {
            DateTimeOffset value = DateTimeOffset.FromUnixTimeSeconds((long)seconds);
            return value.AddTicks(checked((long)microseconds * TimeSpan.TicksPerMicrosecond));
        }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (OverflowException) { return null; }
    }

    internal static TcpState MapTcpState(int state) => state switch
    {
        0 => TcpState.Closed,
        1 => TcpState.Listen,
        2 => TcpState.SynSent,
        3 => TcpState.SynReceived,
        4 => TcpState.Established,
        5 => TcpState.CloseWait,
        6 => TcpState.FinWait1,
        7 => TcpState.Closing,
        8 => TcpState.LastAck,
        9 => TcpState.FinWait2,
        10 => TcpState.TimeWait,
        _ => TcpState.Unknown,
    };

    internal static int ParsePort(int nativeValue)
    {
        ushort networkOrder = unchecked((ushort)nativeValue);
        return BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReverseEndianness(networkOrder)
            : networkOrder;
    }

    internal static string? ParsePath(ReadOnlySpan<byte> bytes, int returnedLength)
    {
        if (returnedLength <= 0 || returnedLength > bytes.Length)
            return null;
        ReadOnlySpan<byte> value = bytes[..returnedLength];
        int nul = value.IndexOf((byte)0);
        if (nul >= 0)
            value = value[..nul];
        if (value.IsEmpty)
            return null;
        return Encoding.UTF8.GetString(value);
    }

    private static IPAddress? ParseAddress(ReadOnlySpan<byte> bytes, byte vflag, ushort interfaceIndex)
    {
        bool ipv4 = (vflag & IniIpv4) != 0;
        bool ipv6 = (vflag & IniIpv6) != 0;
        if (ipv6)
            return new IPAddress(bytes, interfaceIndex);
        if (ipv4)
            return new IPAddress(bytes.Slice(12, 4));
        return null;
    }

    private static string? ReadCString(ReadOnlySpan<byte> bytes)
    {
        int nul = bytes.IndexOf((byte)0);
        ReadOnlySpan<byte> value = nul >= 0 ? bytes[..nul] : bytes;
        return value.IsEmpty ? null : Encoding.UTF8.GetString(value);
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(int)));
    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));
    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));
    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, sizeof(ulong)));

    private static void RequireLength(ReadOnlySpan<byte> bytes, int minimum, string name)
    {
        if (bytes.Length < minimum)
            throw new InvalidDataException($"The macOS {name} value is truncated.");
    }

    private static void RequireExactLength(ReadOnlySpan<byte> bytes, int expected, string name)
    {
        if (bytes.Length != expected)
            throw new InvalidDataException($"The macOS {name} size is incompatible with the expected ABI.");
    }

    internal sealed record RawSocketEndpoint(
        NetworkTransportProtocol Protocol,
        IPAddress LocalAddress,
        int LocalPort,
        IPAddress? RemoteAddress,
        int? RemotePort,
        TcpState? State,
        int ProcessId);

    internal sealed record ParsedProcessInfo(int ProcessId, string? Name, DateTimeOffset? StartedAt);
}
