using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.Platform.Windows;

/// <summary>Reads the Windows IP Helper tables directly; no external programs are started.</summary>
public sealed class WindowsProcessesAndPortsPlatformService : IProcessesAndPortsPlatformService
{
    public ProcessesAndPortsSupportInfo Support { get; } = new(true);

    public ProcessesAndPortsSnapshot CaptureSnapshot(ProcessesAndPortsQuery query, CancellationToken cancellationToken = default)
    {
        var rows = new List<RawEndpoint>();
        if (query.IncludeTcpListeners || query.IncludeOtherTcpConnections)
        {
            rows.AddRange(ReadTcp(false, cancellationToken));
            rows.AddRange(ReadTcp(true, cancellationToken));
        }
        if (query.IncludeUdpEndpoints)
        {
            rows.AddRange(ReadUdp(false, cancellationToken));
            rows.AddRange(ReadUdp(true, cancellationToken));
        }

        var processes = new Dictionary<int, ProcessSnapshot>();
        var endpoints = new List<ProcessNetworkEndpoint>();
        foreach (RawEndpoint row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Protocol == NetworkTransportProtocol.Tcp &&
                !(row.State == TcpState.Listen ? query.IncludeTcpListeners : query.IncludeOtherTcpConnections))
                continue;
            if (!processes.TryGetValue(row.ProcessId, out ProcessSnapshot? process))
                processes[row.ProcessId] = process = ReadProcess(row.ProcessId);
            endpoints.Add(new(row.Protocol, row.LocalAddress, row.LocalPort, row.RemoteAddress, row.RemotePort, row.State, process));
        }
        return new(DateTimeOffset.Now, endpoints);
    }

    public ProcessTerminationResult TerminateProcess(ProcessIdentity identity, CancellationToken cancellationToken = default)
    {
        if (identity.ProcessId == Environment.ProcessId)
            return new(ProcessTerminationStatus.CurrentProcess, "CSharpFar cannot terminate its own process.");
        try
        {
            using Process process = Process.GetProcessById(identity.ProcessId);
            DateTimeOffset? started = TryStartedAt(process);
            if (identity.StartedAt is null || started is null || started.Value != identity.StartedAt.Value)
                return new(ProcessTerminationStatus.StaleIdentity, "The process identity is no longer current.");
            cancellationToken.ThrowIfCancellationRequested();
            process.Kill(entireProcessTree: false);
            return new(ProcessTerminationStatus.Success);
        }
        catch (ArgumentException) { return new(ProcessTerminationStatus.NotFound, "The process has already exited."); }
        catch (InvalidOperationException) { return new(ProcessTerminationStatus.NotFound, "The process has already exited."); }
        catch (System.ComponentModel.Win32Exception ex) { return new(ProcessTerminationStatus.AccessDenied, ex.Message); }
        catch (UnauthorizedAccessException ex) { return new(ProcessTerminationStatus.AccessDenied, ex.Message); }
    }

    private static ProcessSnapshot ReadProcess(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            string? path = null;
            try { path = process.MainModule?.FileName; } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException) { }
            return new(pid, process.ProcessName, path, TryStartedAt(process));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        { return new(pid, null, null, null); }
    }
    private static DateTimeOffset? TryStartedAt(Process process)
    {
        try { return new DateTimeOffset(process.StartTime); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException) { return null; }
    }

    private static IEnumerable<RawEndpoint> ReadTcp(bool ipv6, CancellationToken token)
    {
        byte[] buffer = ReadTable(ipv6, tcp: true, token);
        int count = BitConverter.ToInt32(buffer, 0), size = ipv6 ? 56 : 24;
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested(); int o = 4 + i * size;
            if (o + size > buffer.Length) yield break;
            if (ipv6)
            {
                var local = new IPAddress(buffer.AsSpan(o, 16)); var remote = new IPAddress(buffer.AsSpan(o + 24, 16));
                yield return new(NetworkTransportProtocol.Tcp, local, Port(buffer, o + 20), remote, Port(buffer, o + 44), (TcpState)BitConverter.ToInt32(buffer, o + 48), BitConverter.ToInt32(buffer, o + 52));
            }
            else
            {
                yield return new(NetworkTransportProtocol.Tcp, new IPAddress(buffer.AsSpan(o + 4, 4)), Port(buffer, o + 8), new IPAddress(buffer.AsSpan(o + 12, 4)), Port(buffer, o + 16), (TcpState)BitConverter.ToInt32(buffer, o), BitConverter.ToInt32(buffer, o + 20));
            }
        }
    }
    private static IEnumerable<RawEndpoint> ReadUdp(bool ipv6, CancellationToken token)
    {
        byte[] buffer = ReadTable(ipv6, tcp: false, token);
        int count = BitConverter.ToInt32(buffer, 0), size = ipv6 ? 28 : 12;
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested(); int o = 4 + i * size;
            if (o + size > buffer.Length) yield break;
            if (ipv6) yield return new(NetworkTransportProtocol.Udp, new IPAddress(buffer.AsSpan(o, 16)), Port(buffer, o + 20), null, null, null, BitConverter.ToInt32(buffer, o + 24));
            else yield return new(NetworkTransportProtocol.Udp, new IPAddress(buffer.AsSpan(o, 4)), Port(buffer, o + 4), null, null, null, BitConverter.ToInt32(buffer, o + 8));
        }
    }
    private static int Port(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];
    private static byte[] ReadTable(bool ipv6, bool tcp, CancellationToken token)
    {
        int size = 0; uint result = tcp ? GetExtendedTcpTable(IntPtr.Zero, ref size, false, ipv6 ? 23 : 2, TcpTableClass.OwnerPidAll, 0) : GetExtendedUdpTable(IntPtr.Zero, ref size, false, ipv6 ? 23 : 2, UdpTableClass.OwnerPid, 0);
        if (result is not 0 and not 122) return [];
        for (int i = 0; i < 3; i++)
        {
            token.ThrowIfCancellationRequested(); IntPtr memory = Marshal.AllocHGlobal(size);
            try { result = tcp ? GetExtendedTcpTable(memory, ref size, false, ipv6 ? 23 : 2, TcpTableClass.OwnerPidAll, 0) : GetExtendedUdpTable(memory, ref size, false, ipv6 ? 23 : 2, UdpTableClass.OwnerPid, 0); if (result == 0) { var bytes = new byte[size]; Marshal.Copy(memory, bytes, 0, size); return bytes; } }
            finally { Marshal.FreeHGlobal(memory); }
        }
        return [];
    }
    private enum TcpTableClass { OwnerPidAll = 5 }
    private enum UdpTableClass { OwnerPid = 1 }
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, TcpTableClass tableClass, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int addressFamily, UdpTableClass tableClass, uint reserved);
    private sealed record RawEndpoint(NetworkTransportProtocol Protocol, IPAddress LocalAddress, int LocalPort, IPAddress? RemoteAddress, int? RemotePort, TcpState? State, int ProcessId);
}
