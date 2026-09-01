namespace CSharpFar.Platform.Abstractions;

public enum FileUsageState { Free, InUse, Blocked, Unavailable }
public enum FileUsageProbeStatus { Allowed, Blocked, Unknown }
public enum FileUsageOperation { Read, Write, Delete, Rename }
public enum FileUsageErrorKind { Unsupported, InvalidPath, NotFound, AccessDenied, Cancelled, PlatformError }
public enum FileUsageOwnerKind { Application, Service, Unknown }

public sealed record FileUsageSupportInfo(bool IsSupported, bool CanReleaseOwners, string? Reason = null, string? ReleaseUnavailableReason = null);
public sealed record FileUsageError(FileUsageErrorKind Kind, string Message, int? PlatformErrorCode = null);
public sealed record FileUsageProbe(FileUsageOperation Operation, FileUsageProbeStatus Status, FileUsageError? Error = null);

public sealed record FileUsageOwnerEntry(
    ProcessSnapshot Process,
    FileUsageOwnerKind Kind = FileUsageOwnerKind.Unknown,
    string? ServiceName = null,
    bool? IsRestartable = null,
    string? MetadataUnavailableReason = null);

public sealed record FileUsageSnapshot(
    string Path,
    DateTimeOffset CapturedAt,
    FileUsageState State,
    IReadOnlyList<FileUsageOwnerEntry> Owners,
    IReadOnlyList<FileUsageProbe> Probes,
    FileUsageError? Error = null);

public sealed record FileUsageReleaseRequest(ProcessIdentity Identity, FileUsageOwnerKind OwnerKind = FileUsageOwnerKind.Application);
public enum FileUsageReleaseStatus { Success, AlreadyExited, AccessDenied, StaleIdentity, CurrentProcess, IneligibleOwner, NotSupported, Failed }
public sealed record FileUsageReleaseResult(FileUsageReleaseStatus Status, string? Message = null);

public interface IFileUsagePlatformService
{
    FileUsageSupportInfo Support { get; }
    FileUsageSnapshot Inspect(string path, CancellationToken cancellationToken = default);
    FileUsageReleaseResult Release(FileUsageReleaseRequest request, CancellationToken cancellationToken = default);
}

public sealed class UnsupportedFileUsagePlatformService(string reason = "File Usage is supported on Windows only.") : IFileUsagePlatformService
{
    public FileUsageSupportInfo Support { get; } = new(false, false, reason, reason);

    public FileUsageSnapshot Inspect(string path, CancellationToken cancellationToken = default) =>
        new(path, DateTimeOffset.Now, FileUsageState.Unavailable, [], [], new FileUsageError(FileUsageErrorKind.Unsupported, Support.Reason!));

    public FileUsageReleaseResult Release(FileUsageReleaseRequest request, CancellationToken cancellationToken = default) =>
        new(FileUsageReleaseStatus.NotSupported, Support.ReleaseUnavailableReason);
}
