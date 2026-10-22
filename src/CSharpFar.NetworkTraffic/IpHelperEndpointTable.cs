using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace CSharpFar.NetworkTraffic;

internal static class IpHelperEndpointTable
{
    private const uint AfInet = 2;
    private const uint AfInet6 = 23;
    private const uint ErrorInsufficientBuffer = 122;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;

    public static IReadOnlyList<IpHelperEndpoint> ReadSnapshot()
    {
        var endpoints = new List<IpHelperEndpoint>();
        ReadTcp(AfInet, "IPv4", endpoints);
        ReadTcp(AfInet6, "IPv6", endpoints);
        ReadUdp(AfInet, "IPv4", endpoints);
        ReadUdp(AfInet6, "IPv6", endpoints);
        return endpoints;
    }

    private static void ReadTcp(uint addressFamily, string family, List<IpHelperEndpoint> endpoints)
    {
        IntPtr table = GetTcpTable(addressFamily);
        try
        {
            int count = checked((int)ReadUInt32(table, 0));
            int rowSize = family == "IPv4" ? 24 : 56;
            for (int i = 0; i < count; i++)
            {
                int row = checked(4 + i * rowSize);
                if (family == "IPv4")
                {
                    uint state = ReadUInt32(table, row);
                    string localAddress = new IPAddress(ReadBytes(table, row + 4, 4)).ToString();
                    int localPort = DecodePort(ReadUInt32(table, row + 8));
                    string remoteAddress = new IPAddress(ReadBytes(table, row + 12, 4)).ToString();
                    int remotePort = DecodePort(ReadUInt32(table, row + 16));
                    int processId = checked((int)ReadUInt32(table, row + 20));
                    endpoints.Add(new IpHelperEndpoint("TCP", family, localAddress, localPort, remoteAddress, remotePort, state.ToString(), processId));
                }
                else
                {
                    string localAddress = new IPAddress(ReadBytes(table, row, 16), ReadUInt32(table, row + 16)).ToString();
                    int localPort = DecodePort(ReadUInt32(table, row + 20));
                    string remoteAddress = new IPAddress(ReadBytes(table, row + 24, 16), ReadUInt32(table, row + 40)).ToString();
                    int remotePort = DecodePort(ReadUInt32(table, row + 44));
                    uint state = ReadUInt32(table, row + 48);
                    int processId = checked((int)ReadUInt32(table, row + 52));
                    endpoints.Add(new IpHelperEndpoint("TCP", family, localAddress, localPort, remoteAddress, remotePort, state.ToString(), processId));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    private static void ReadUdp(uint addressFamily, string family, List<IpHelperEndpoint> endpoints)
    {
        IntPtr table = GetUdpTable(addressFamily);
        try
        {
            int count = checked((int)ReadUInt32(table, 0));
            int rowSize = family == "IPv4" ? 12 : 28;
            for (int i = 0; i < count; i++)
            {
                int row = checked(4 + i * rowSize);
                string localAddress;
                int localPort;
                int processId;
                if (family == "IPv4")
                {
                    localAddress = new IPAddress(ReadBytes(table, row, 4)).ToString();
                    localPort = DecodePort(ReadUInt32(table, row + 4));
                    processId = checked((int)ReadUInt32(table, row + 8));
                }
                else
                {
                    localAddress = new IPAddress(ReadBytes(table, row, 16), ReadUInt32(table, row + 16)).ToString();
                    localPort = DecodePort(ReadUInt32(table, row + 20));
                    processId = checked((int)ReadUInt32(table, row + 24));
                }
                endpoints.Add(new IpHelperEndpoint("UDP", family, localAddress, localPort, null, null, null, processId));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    private static IntPtr GetTcpTable(uint addressFamily)
    {
        uint size = 0;
        uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, addressFamily, TcpTableOwnerPidAll, 0);
        if (result != ErrorInsufficientBuffer)
            throw new Win32Exception(checked((int)result), "GetExtendedTcpTable size query failed.");

        for (int attempt = 0; attempt < 3; attempt++)
        {
            IntPtr table = Marshal.AllocHGlobal(checked((int)size));
            result = GetExtendedTcpTable(table, ref size, false, addressFamily, TcpTableOwnerPidAll, 0);
            if (result == 0)
                return table;
            Marshal.FreeHGlobal(table);
            if (result != ErrorInsufficientBuffer)
                throw new Win32Exception(checked((int)result), "GetExtendedTcpTable failed.");
        }

        throw new Win32Exception(checked((int)ErrorInsufficientBuffer), "The TCP endpoint table changed during repeated reads.");
    }

    private static IntPtr GetUdpTable(uint addressFamily)
    {
        uint size = 0;
        uint result = GetExtendedUdpTable(IntPtr.Zero, ref size, false, addressFamily, UdpTableOwnerPid, 0);
        if (result != ErrorInsufficientBuffer)
            throw new Win32Exception(checked((int)result), "GetExtendedUdpTable size query failed.");

        for (int attempt = 0; attempt < 3; attempt++)
        {
            IntPtr table = Marshal.AllocHGlobal(checked((int)size));
            result = GetExtendedUdpTable(table, ref size, false, addressFamily, UdpTableOwnerPid, 0);
            if (result == 0)
                return table;
            Marshal.FreeHGlobal(table);
            if (result != ErrorInsufficientBuffer)
                throw new Win32Exception(checked((int)result), "GetExtendedUdpTable failed.");
        }

        throw new Win32Exception(checked((int)ErrorInsufficientBuffer), "The UDP endpoint table changed during repeated reads.");
    }

    private static uint ReadUInt32(IntPtr address, int offset) => unchecked((uint)Marshal.ReadInt32(address, offset));

    private static byte[] ReadBytes(IntPtr address, int offset, int count)
    {
        byte[] bytes = new byte[count];
        Marshal.Copy(IntPtr.Add(address, offset), bytes, 0, count);
        return bytes;
    }

    private static int DecodePort(uint raw) => BinaryPrimitives.ReverseEndianness(unchecked((ushort)raw));

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        uint addressFamily,
        int tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr udpTable,
        ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        uint addressFamily,
        int tableClass,
        uint reserved);
}

internal sealed record IpHelperEndpoint(
    string Protocol,
    string Family,
    string LocalAddress,
    int LocalPort,
    string? RemoteAddress,
    int? RemotePort,
    string? State,
    int ProcessId);
