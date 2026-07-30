using System.Buffers.Binary;
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
    private const uint ErrorInsufficientBuffer = 122;
    private const int MaxTableReadAttempts = 3;

    public ProcessesAndPortsSupportInfo Support { get; } = new(true, true);

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
            if (started is null || started.Value != identity.StartedAt)
                return new(ProcessTerminationStatus.StaleIdentity, "The process identity is no longer current.");
            cancellationToken.ThrowIfCancellationRequested();
            process.Kill(entireProcessTree: false);
            return new(ProcessTerminationStatus.Success);
        }
        catch (ArgumentException) { return new(ProcessTerminationStatus.NotFound, "The process has already exited."); }
        catch (InvalidOperationException) { return new(ProcessTerminationStatus.AlreadyExited, "The process has already exited."); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5) { return new(ProcessTerminationStatus.AccessDenied, ex.Message); }
        catch (Win32Exception ex) { return new(ProcessTerminationStatus.Failed, ex.Message); }
        catch (UnauthorizedAccessException ex) { return new(ProcessTerminationStatus.AccessDenied, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(ProcessTerminationStatus.Failed, ex.Message); }
    }

    private static ProcessSnapshot ReadProcess(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            string? name = null;
            string? path = null;
            DateTimeOffset? startedAt = null;
            ProcessMetadataStatus status = ProcessMetadataStatus.Available;
            try { name = process.ProcessName; } catch (Exception ex) when (IsMetadataFailure(ex)) { status = MetadataStatus(ex); }
            try { path = process.MainModule?.FileName; } catch (Exception ex) when (IsMetadataFailure(ex)) { status = CombineMetadataStatus(status, MetadataStatus(ex)); }
            try { startedAt = TryStartedAt(process); } catch (Exception ex) when (IsMetadataFailure(ex)) { status = CombineMetadataStatus(status, MetadataStatus(ex)); }
            if (name is null || path is null || startedAt is null)
                status = CombineMetadataStatus(status, ProcessMetadataStatus.Partial);
            return new(pid, name, path, startedAt, status);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        { return new(pid, null, null, null, MetadataStatus(ex)); }
    }

    private static bool IsMetadataFailure(Exception ex) => ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException;
    private static ProcessMetadataStatus MetadataStatus(Exception ex) => ex is UnauthorizedAccessException or Win32Exception { NativeErrorCode: 5 }
        ? ProcessMetadataStatus.AccessDenied : ex is ArgumentException or InvalidOperationException ? ProcessMetadataStatus.Exited : ProcessMetadataStatus.Unavailable;
    private static ProcessMetadataStatus CombineMetadataStatus(ProcessMetadataStatus current, ProcessMetadataStatus next) =>
        current is ProcessMetadataStatus.AccessDenied or ProcessMetadataStatus.Exited ? current : next;
    private static DateTimeOffset? TryStartedAt(Process process)
    {
        try { return new DateTimeOffset(process.StartTime); }
        catch (Exception ex) when (IsMetadataFailure(ex)) { return null; }
    }

    private static IEnumerable<RawEndpoint> ReadTcp(bool ipv6, CancellationToken token)
    {
        byte[] buffer = ReadTable(ipv6, tcp: true, token);
        foreach (ReadOnlyMemory<byte> row in Rows(buffer, ipv6 ? 56 : 24))
        {
            token.ThrowIfCancellationRequested();
            yield return ipv6 ? ParseTcp6Row(row.Span) : ParseTcp4Row(row.Span);
        }
    }

    private static IEnumerable<RawEndpoint> ReadUdp(bool ipv6, CancellationToken token)
    {
        byte[] buffer = ReadTable(ipv6, tcp: false, token);
        foreach (ReadOnlyMemory<byte> row in Rows(buffer, ipv6 ? 28 : 12))
        {
            token.ThrowIfCancellationRequested();
            yield return ipv6 ? ParseUdp6Row(row.Span) : ParseUdp4Row(row.Span);
        }
    }

    internal static RawEndpoint ParseTcp4Row(ReadOnlySpan<byte> row)
    {
        RequireRow(row, 24);
        TcpState state = (TcpState)BinaryPrimitives.ReadInt32LittleEndian(row);
        return new(NetworkTransportProtocol.Tcp, new IPAddress(row.Slice(4, 4)), Port(row, 8), new IPAddress(row.Slice(12, 4)), Port(row, 16), state, BinaryPrimitives.ReadInt32LittleEndian(row.Slice(20)));
    }

    internal static RawEndpoint ParseUdp4Row(ReadOnlySpan<byte> row)
    {
        RequireRow(row, 12);
        return new(NetworkTransportProtocol.Udp, new IPAddress(row.Slice(0, 4)), Port(row, 4), null, null, null, BinaryPrimitives.ReadInt32LittleEndian(row.Slice(8)));
    }

    internal static RawEndpoint ParseTcp6Row(ReadOnlySpan<byte> row)
    {
        RequireRow(row, 56);
        var local = new IPAddress(row.Slice(0, 16), BinaryPrimitives.ReadUInt32BigEndian(row.Slice(16, 4)));
        TcpState state = (TcpState)BinaryPrimitives.ReadInt32LittleEndian(row.Slice(48, 4));
        IPAddress? remote = state == TcpState.Listen ? null : new IPAddress(row.Slice(24, 16), BinaryPrimitives.ReadUInt32BigEndian(row.Slice(40, 4)));
        int? remotePort = state == TcpState.Listen ? null : Port(row, 44);
        return new(NetworkTransportProtocol.Tcp, local, Port(row, 20), remote, remotePort, state, BinaryPrimitives.ReadInt32LittleEndian(row.Slice(52)));
    }

    internal static RawEndpoint ParseUdp6Row(ReadOnlySpan<byte> row)
    {
        RequireRow(row, 28);
        return new(NetworkTransportProtocol.Udp, new IPAddress(row.Slice(0, 16), BinaryPrimitives.ReadUInt32BigEndian(row.Slice(16, 4))), Port(row, 20), null, null, null, BinaryPrimitives.ReadInt32LittleEndian(row.Slice(24)));
    }

    private static IEnumerable<ReadOnlyMemory<byte>> Rows(byte[] buffer, int rowSize)
    {
        if (buffer.Length < sizeof(uint))
            throw new InvalidDataException("The Windows endpoint table is truncated.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        if (count < 0)
            throw new InvalidDataException("The Windows endpoint table contains an invalid row count.");
        int required;
        try { required = checked(sizeof(uint) + checked(count * rowSize)); }
        catch (OverflowException ex) { throw new InvalidDataException("The Windows endpoint table size is invalid.", ex); }
        if (required > buffer.Length)
            throw new InvalidDataException("The Windows endpoint table is truncated.");
        for (int i = 0; i < count; i++) yield return new ReadOnlyMemory<byte>(buffer, sizeof(uint) + i * rowSize, rowSize);
    }

    private static void RequireRow(ReadOnlySpan<byte> row, int size)
    {
        if (row.Length < size)
            throw new InvalidDataException("The Windows endpoint row is truncated.");
    }
    private static int Port(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    private static byte[] ReadTable(bool ipv6, bool tcp, CancellationToken token)
    {
        int size = 0;
        uint result = InvokeTable(tcp, IntPtr.Zero, ref size, ipv6);
        if (result != ErrorInsufficientBuffer)
            throw new Win32Exception((int)result, "Unable to determine Windows endpoint table size.");
        if (size < sizeof(uint))
            throw new InvalidDataException("Windows returned an invalid endpoint table size.");

        for (int attempt = 0; attempt < MaxTableReadAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            IntPtr memory = Marshal.AllocHGlobal(size);
            try
            {
                int returnedSize = size;
                result = InvokeTable(tcp, memory, ref returnedSize, ipv6);
                if (result == 0)
                {
                    if (returnedSize < sizeof(uint) || returnedSize > size)
                        throw new InvalidDataException("Windows returned an invalid endpoint table length.");
                    var bytes = new byte[returnedSize];
                    Marshal.Copy(memory, bytes, 0, returnedSize);
                    return bytes;
                }
                if (result != ErrorInsufficientBuffer)
                    throw new Win32Exception((int)result, "Unable to read Windows endpoint table.");
                if (returnedSize <= size)
                    throw new InvalidDataException("Windows did not provide a larger endpoint table size.");
                size = returnedSize;
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        throw new InvalidOperationException("The Windows endpoint table changed too frequently to capture.");
    }

    private static uint InvokeTable(bool tcp, IntPtr table, ref int size, bool ipv6) => tcp
        ? GetExtendedTcpTable(table, ref size, false, ipv6 ? 23 : 2, TcpTableClass.OwnerPidAll, 0)
        : GetExtendedUdpTable(table, ref size, false, ipv6 ? 23 : 2, UdpTableClass.OwnerPid, 0);
    private enum TcpTableClass { OwnerPidAll = 5 }
    private enum UdpTableClass { OwnerPid = 1 }
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, TcpTableClass tableClass, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int addressFamily, UdpTableClass tableClass, uint reserved);
    internal sealed record RawEndpoint(NetworkTransportProtocol Protocol, IPAddress LocalAddress, int LocalPort, IPAddress? RemoteAddress, int? RemotePort, TcpState? State, int ProcessId);
}
