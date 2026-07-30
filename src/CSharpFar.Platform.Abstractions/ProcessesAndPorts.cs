using System.Net;
using System.Net.NetworkInformation;

namespace CSharpFar.Platform.Abstractions;

public enum NetworkTransportProtocol { Tcp, Udp }
public sealed record ProcessesAndPortsQuery(bool IncludeTcpListeners = true, bool IncludeUdpEndpoints = true, bool IncludeOtherTcpConnections = false);
public sealed record ProcessesAndPortsSupportInfo(bool IsSupported, bool CanTerminate, string? Reason = null, string? TerminationUnavailableReason = null);
public sealed record ProcessIdentity(int ProcessId, DateTimeOffset StartedAt);
public enum ProcessMetadataStatus { Available, Partial, AccessDenied, Exited, Unavailable }
public sealed record ProcessSnapshot(int ProcessId, string? Name, string? ExecutablePath, DateTimeOffset? StartedAt, ProcessMetadataStatus MetadataStatus = ProcessMetadataStatus.Available)
{
    public ProcessIdentity? Identity => StartedAt is { } startedAt ? new(ProcessId, startedAt) : null;
}
public sealed record ProcessNetworkEndpoint(NetworkTransportProtocol Protocol, IPAddress LocalAddress, int LocalPort, IPAddress? RemoteAddress, int? RemotePort, TcpState? TcpState, ProcessSnapshot Process);
public sealed record ProcessesAndPortsSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<ProcessNetworkEndpoint> Endpoints);
public enum ProcessTerminationStatus { Success, NotFound, AlreadyExited, AccessDenied, StaleIdentity, CurrentProcess, NotSupported, Failed }
public sealed record ProcessTerminationResult(ProcessTerminationStatus Status, string? Message = null);

public interface IProcessesAndPortsPlatformService
{
    ProcessesAndPortsSupportInfo Support { get; }
    ProcessesAndPortsSnapshot CaptureSnapshot(ProcessesAndPortsQuery query, CancellationToken cancellationToken = default);
    ProcessTerminationResult TerminateProcess(ProcessIdentity identity, CancellationToken cancellationToken = default);
}

public sealed class UnsupportedProcessesAndPortsPlatformService(string reason = "Processes and Ports is supported on Windows only.") : IProcessesAndPortsPlatformService
{
    public ProcessesAndPortsSupportInfo Support { get; } = new(false, false, reason, reason);
    public ProcessesAndPortsSnapshot CaptureSnapshot(ProcessesAndPortsQuery query, CancellationToken cancellationToken = default) => new(DateTimeOffset.Now, []);
    public ProcessTerminationResult TerminateProcess(ProcessIdentity identity, CancellationToken cancellationToken = default) => new(ProcessTerminationStatus.NotSupported, Support.Reason);
}
