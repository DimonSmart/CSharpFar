using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.Platform.MacOs;

/// <summary>Reads macOS process descriptors and socket metadata directly through libproc.</summary>
public sealed class MacOsProcessesAndPortsPlatformService : IProcessesAndPortsPlatformService
{
    private const int MaxEnumerationAttempts = 3;
    private const int PidSlack = 32;
    private const int FdSlack = 16;
    private const int PidPathBufferSize = 4096;
    private readonly IMacOsProcessesAndPortsNative _native;

    public MacOsProcessesAndPortsPlatformService() : this(new MacOsLibProc()) { }
    internal MacOsProcessesAndPortsPlatformService(IMacOsProcessesAndPortsNative native) => _native = native;

    public ProcessesAndPortsSupportInfo Support { get; } = new(true, true);

    public ProcessesAndPortsSnapshot CaptureSnapshot(ProcessesAndPortsQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<int> pids = ReadPids(cancellationToken);
        var endpoints = new List<ProcessNetworkEndpoint>();

        foreach (int pid in pids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pid <= 0)
                continue;

            List<MacOsProcessesAndPortsParser.RawSocketEndpoint> raw = ReadEndpoints(pid, query, cancellationToken);
            if (raw.Count == 0)
                continue;

            ProcessSnapshot process = ReadProcess(pid);
            foreach (MacOsProcessesAndPortsParser.RawSocketEndpoint endpoint in raw)
            {
                endpoints.Add(new(
                    endpoint.Protocol,
                    endpoint.LocalAddress,
                    endpoint.LocalPort,
                    endpoint.RemoteAddress,
                    endpoint.RemotePort,
                    endpoint.State,
                    process));
            }
        }

        return new(DateTimeOffset.UtcNow, endpoints);
    }

    public ProcessTerminationResult TerminateProcess(ProcessIdentity identity, CancellationToken cancellationToken = default)
    {
        if (identity.ProcessId == Environment.ProcessId)
            return new(ProcessTerminationStatus.CurrentProcess, "CSharpFar cannot terminate its own process.");

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using Process process = Process.GetProcessById(identity.ProcessId);
            cancellationToken.ThrowIfCancellationRequested();

            ProcessIdentityRead identityRead = ReadIdentity(identity.ProcessId);
            if (identityRead.Status == ProcessIdentityReadStatus.NotFound)
                return new(ProcessTerminationStatus.NotFound, "The process has already exited.");
            if (identityRead.Status == ProcessIdentityReadStatus.AccessDenied)
                return new(ProcessTerminationStatus.AccessDenied, "Process identity is not accessible.");
            if (identityRead.Status != ProcessIdentityReadStatus.Available || identityRead.StartedAt is null)
                return new(ProcessTerminationStatus.Failed, "Unable to verify the current process identity.");
            if (identityRead.StartedAt.Value != identity.StartedAt)
                return new(ProcessTerminationStatus.StaleIdentity, "The process identity is no longer current.");

            cancellationToken.ThrowIfCancellationRequested();
            process.Kill(entireProcessTree: false);
            return new(ProcessTerminationStatus.Success);
        }
        catch (ArgumentException) { return new(ProcessTerminationStatus.NotFound, "The process has already exited."); }
        catch (InvalidOperationException) { return new(ProcessTerminationStatus.AlreadyExited, "The process has already exited."); }
        catch (Win32Exception ex) when (IsAccessDenied(ex.NativeErrorCode)) { return new(ProcessTerminationStatus.AccessDenied, ex.Message); }
        catch (Win32Exception ex) { return new(ProcessTerminationStatus.Failed, ex.Message); }
        catch (UnauthorizedAccessException ex) { return new(ProcessTerminationStatus.AccessDenied, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(ProcessTerminationStatus.Failed, ex.Message); }
    }

    private IReadOnlyList<int> ReadPids(CancellationToken token)
    {
        int capacity = 0;
        for (int attempt = 0; attempt < MaxEnumerationAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            MacOsNativeResult sizing = _native.ListAllPids(IntPtr.Zero, 0);
            if (sizing.Value <= 0)
                throw NativeCaptureFailure("Unable to enumerate macOS processes.", sizing);

            capacity = checked(Math.Max(capacity, checked(sizing.Value + PidSlack)));
            int byteLength = checked(capacity * sizeof(int));
            IntPtr buffer = Marshal.AllocHGlobal(byteLength);
            try
            {
                MacOsNativeResult result = _native.ListAllPids(buffer, byteLength);
                if (result.Value <= 0)
                    throw NativeCaptureFailure("Unable to enumerate macOS processes.", result);
                if (result.Value > capacity)
                    throw new InvalidDataException("macOS returned more PIDs than the supplied buffer can contain.");

                int[] pids = new int[result.Value];
                Marshal.Copy(buffer, pids, 0, result.Value);
                if (pids.All(static pid => pid <= 0))
                    throw new InvalidDataException("macOS returned an invalid empty process list.");
                if (result.Value < capacity)
                    return pids;

                capacity = checked(capacity * 2);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        throw new InvalidOperationException("The macOS process list changed too frequently to capture.");
    }

    private List<MacOsProcessesAndPortsParser.RawSocketEndpoint> ReadEndpoints(
        int pid, ProcessesAndPortsQuery query, CancellationToken token)
    {
        IReadOnlyList<(int Fd, uint Type)> fds = ReadFileDescriptors(pid, token);
        var endpoints = new List<MacOsProcessesAndPortsParser.RawSocketEndpoint>();

        foreach ((int fd, uint type) in fds)
        {
            token.ThrowIfCancellationRequested();
            if (type != MacOsProcessesAndPortsNative.ProxFdTypeSocket || fd < 0)
                continue;

            MacOsProcessesAndPortsParser.RawSocketEndpoint? endpoint = ReadSocket(pid, fd);
            if (endpoint is null || !Matches(query, endpoint))
                continue;
            endpoints.Add(endpoint);
        }
        return endpoints;
    }

    private IReadOnlyList<(int Fd, uint Type)> ReadFileDescriptors(int pid, CancellationToken token)
    {
        int requestedSize = 0;
        IReadOnlyList<(int Fd, uint Type)> last = [];

        for (int attempt = 0; attempt < MaxEnumerationAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            MacOsNativeResult sizing = _native.PidInfo(pid, MacOsProcessesAndPortsNative.ProcPidListFds, 0, IntPtr.Zero, 0);
            if (sizing.Value == 0)
                return HandleEmptyFdResult(sizing);
            if (sizing.Value < 0)
                return [];
            if (sizing.Value % MacOsProcessesAndPortsParser.ProcFdInfoSize != 0)
                throw new InvalidDataException("macOS returned a partial proc_fdinfo entry.");

            int minimumEntries = sizing.Value / MacOsProcessesAndPortsParser.ProcFdInfoSize;
            int entries = checked(minimumEntries + FdSlack);
            requestedSize = Math.Max(requestedSize, checked(entries * MacOsProcessesAndPortsParser.ProcFdInfoSize));

            IntPtr buffer = Marshal.AllocHGlobal(requestedSize);
            try
            {
                MacOsNativeResult result = _native.PidInfo(
                    pid, MacOsProcessesAndPortsNative.ProcPidListFds, 0, buffer, requestedSize);
                if (result.Value == 0)
                    return HandleEmptyFdResult(result);
                if (result.Value < 0)
                    return [];
                if (result.Value > requestedSize || result.Value % MacOsProcessesAndPortsParser.ProcFdInfoSize != 0)
                    throw new InvalidDataException("macOS returned an invalid file-descriptor list length.");

                byte[] bytes = new byte[result.Value];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                var parsed = new List<(int Fd, uint Type)>(bytes.Length / MacOsProcessesAndPortsParser.ProcFdInfoSize);
                for (int offset = 0; offset < bytes.Length; offset += MacOsProcessesAndPortsParser.ProcFdInfoSize)
                    parsed.Add(MacOsProcessesAndPortsParser.ParseFd(bytes.AsSpan(offset, MacOsProcessesAndPortsParser.ProcFdInfoSize)));
                last = parsed;

                if (result.Value < requestedSize)
                    return parsed;
                requestedSize = checked(requestedSize * 2);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        return last;
    }

    private static IReadOnlyList<(int Fd, uint Type)> HandleEmptyFdResult(MacOsNativeResult result) =>
        MacOsNativeError.Classify(result.Error) switch
        {
            MacOsNativeErrorKind.None or MacOsNativeErrorKind.NotFound or MacOsNativeErrorKind.AccessDenied => [],
            MacOsNativeErrorKind.NoMemory or MacOsNativeErrorKind.Overflow or MacOsNativeErrorKind.Other => [],
            _ => [],
        };

    private MacOsProcessesAndPortsParser.RawSocketEndpoint? ReadSocket(int pid, int fd)
    {
        int size = MacOsProcessesAndPortsParser.SocketFdInfoSize;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            MacOsNativeResult result = _native.PidFdInfo(
                pid, fd, MacOsProcessesAndPortsNative.ProcPidFdSocketInfo, buffer, size);
            if (result.Value == 0)
                return null;
            if (result.Value != size)
                throw new InvalidDataException(
                    $"macOS returned {result.Value} bytes for socket_fdinfo; expected {size}.");

            byte[] bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, size);
            return MacOsProcessesAndPortsParser.ParseSocket(bytes, pid);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private ProcessSnapshot ReadProcess(int pid)
    {
        ProcessMetadataRead metadata = ReadBsdMetadata(pid);
        if (metadata.Info is null)
            return new(pid, null, null, null, metadata.Status);

        string? path = null;
        ProcessMetadataStatus status = metadata.Status;
        IntPtr buffer = Marshal.AllocHGlobal(PidPathBufferSize);
        try
        {
            MacOsNativeResult result = _native.PidPath(pid, buffer, PidPathBufferSize);
            if (result.Value > 0)
            {
                if (result.Value > PidPathBufferSize)
                    throw new InvalidDataException("macOS returned an invalid process path length.");
                byte[] bytes = new byte[PidPathBufferSize];
                Marshal.Copy(buffer, bytes, 0, PidPathBufferSize);
                path = MacOsProcessesAndPortsParser.ParsePath(bytes, result.Value);
            }
            else if (result.Error is MacOsProcessesAndPortsNative.EPerm or MacOsProcessesAndPortsNative.EAccess)
                status = CombineStatus(status, ProcessMetadataStatus.AccessDenied);
            else if (result.Error == MacOsProcessesAndPortsNative.ESrch)
                status = CombineStatus(status, ProcessMetadataStatus.Exited);
            else if (result.Error != 0)
                status = CombineStatus(status, ProcessMetadataStatus.Partial);
        }
        finally { Marshal.FreeHGlobal(buffer); }

        if (metadata.Info.Name is null || metadata.Info.StartedAt is null || path is null)
            status = CombineStatus(status, ProcessMetadataStatus.Partial);
        return new(pid, metadata.Info.Name, path, metadata.Info.StartedAt, status);
    }

    private ProcessMetadataRead ReadBsdMetadata(int pid)
    {
        int size = MacOsProcessesAndPortsParser.ProcBsdInfoSize;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            MacOsNativeResult result = _native.PidInfo(
                pid, MacOsProcessesAndPortsNative.ProcPidTBsdInfo, 0, buffer, size);
            if (result.Value == 0)
                return new(null, MetadataStatus(result.Error));
            if (result.Value != size)
                throw new InvalidDataException(
                    $"macOS returned {result.Value} bytes for proc_bsdinfo; expected {size}.");

            byte[] bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, size);
            MacOsProcessesAndPortsParser.ParsedProcessInfo info = MacOsProcessesAndPortsParser.ParseBsdInfo(bytes, pid);
            ProcessMetadataStatus status =
                info.Name is null || info.StartedAt is null ? ProcessMetadataStatus.Partial : ProcessMetadataStatus.Available;
            return new(info, status);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private ProcessIdentityRead ReadIdentity(int pid)
    {
        try
        {
            ProcessMetadataRead metadata = ReadBsdMetadata(pid);
            if (metadata.Info?.StartedAt is { } startedAt)
                return new(ProcessIdentityReadStatus.Available, startedAt);
            return metadata.Status switch
            {
                ProcessMetadataStatus.Exited => new(ProcessIdentityReadStatus.NotFound, null),
                ProcessMetadataStatus.AccessDenied => new(ProcessIdentityReadStatus.AccessDenied, null),
                _ => new(ProcessIdentityReadStatus.Unavailable, null),
            };
        }
        catch (InvalidDataException)
        {
            return new(ProcessIdentityReadStatus.Unavailable, null);
        }
    }

    private static bool Matches(ProcessesAndPortsQuery query, MacOsProcessesAndPortsParser.RawSocketEndpoint endpoint) =>
        endpoint.Protocol switch
        {
            NetworkTransportProtocol.Udp => query.IncludeUdpEndpoints,
            NetworkTransportProtocol.Tcp when endpoint.State == TcpState.Listen => query.IncludeTcpListeners,
            NetworkTransportProtocol.Tcp => query.IncludeOtherTcpConnections,
            _ => false,
        };

    private static Exception NativeCaptureFailure(string message, MacOsNativeResult result) =>
        new InvalidOperationException(result.Error == 0 ? message : $"{message} errno={result.Error}.");

    private static ProcessMetadataStatus MetadataStatus(int error) =>
        MacOsNativeError.Classify(error) switch
        {
            MacOsNativeErrorKind.NotFound => ProcessMetadataStatus.Exited,
            MacOsNativeErrorKind.AccessDenied => ProcessMetadataStatus.AccessDenied,
            _ => ProcessMetadataStatus.Unavailable,
        };

    private static ProcessMetadataStatus CombineStatus(ProcessMetadataStatus current, ProcessMetadataStatus next)
    {
        if (current == ProcessMetadataStatus.Exited || next == ProcessMetadataStatus.Exited)
            return ProcessMetadataStatus.Exited;
        if (current == ProcessMetadataStatus.AccessDenied || next == ProcessMetadataStatus.AccessDenied)
            return ProcessMetadataStatus.AccessDenied;
        if (current == ProcessMetadataStatus.Unavailable || next == ProcessMetadataStatus.Unavailable)
            return ProcessMetadataStatus.Unavailable;
        if (current == ProcessMetadataStatus.Partial || next == ProcessMetadataStatus.Partial)
            return ProcessMetadataStatus.Partial;
        return ProcessMetadataStatus.Available;
    }

    private static bool IsAccessDenied(int error) =>
        MacOsNativeError.Classify(error) == MacOsNativeErrorKind.AccessDenied;

    private sealed record ProcessMetadataRead(
        MacOsProcessesAndPortsParser.ParsedProcessInfo? Info,
        ProcessMetadataStatus Status);

    private enum ProcessIdentityReadStatus { Available, NotFound, AccessDenied, Unavailable }
    private sealed record ProcessIdentityRead(ProcessIdentityReadStatus Status, DateTimeOffset? StartedAt);
}
