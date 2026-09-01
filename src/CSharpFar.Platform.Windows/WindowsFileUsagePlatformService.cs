using System.ComponentModel;
using System.Runtime.InteropServices;
using CSharpFar.Platform.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace CSharpFar.Platform.Windows;

public sealed class WindowsFileUsagePlatformService : IFileUsagePlatformService
{
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int MaxResizeAttempts = 5;
    private readonly IRestartManagerNative _native;
    private readonly IProcessSnapshotReader _processes;
    private readonly IFileAccessNative _fileAccess;
    private readonly IProcessesAndPortsPlatformService _termination;

    public WindowsFileUsagePlatformService() : this(new RestartManagerNative(), new ProcessSnapshotReader(),
        new FileAccessNative(), new WindowsProcessesAndPortsPlatformService())
    { }

    internal WindowsFileUsagePlatformService(IRestartManagerNative native, IProcessSnapshotReader processes,
        IFileAccessNative? fileAccess = null, IProcessesAndPortsPlatformService? termination = null)
    {
        _native = native;
        _processes = processes;
        _fileAccess = fileAccess ?? new FileAccessNative();
        _termination = termination ?? new WindowsProcessesAndPortsPlatformService();
    }

    public FileUsageSupportInfo Support => new(true, _termination.Support.CanTerminate, null,
        _termination.Support.TerminationUnavailableReason);

    public FileUsageSnapshot Inspect(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Failure(path, FileUsageErrorKind.InvalidPath, "A file path is required.");

        uint session = 0;
        bool started = false;
        FileUsageSnapshot? result = null;
        IReadOnlyList<FileUsageProbe> probes = Probe(path);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = Guid.NewGuid().ToString("N");
            Check(_native.StartSession(out session, key), "start a Restart Manager session");
            started = true;

            cancellationToken.ThrowIfCancellationRequested();
            Check(_native.RegisterResources(session, [path]), "register the file with Restart Manager");

            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<RestartManagerProcessInfo> processInfos = GetOwners(session, cancellationToken);
            var owners = new List<FileUsageOwnerEntry>(processInfos.Count);
            foreach (RestartManagerProcessInfo info in processInfos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProcessSnapshot metadata = _processes.Read(info.ProcessId);
                DateTimeOffset? nativeStartedAt = FromFileTime(info.ProcessStartTime);
                var process = new ProcessSnapshot(
                    info.ProcessId,
                    metadata.Name ?? NullIfEmpty(info.ApplicationName),
                    metadata.ExecutablePath,
                    metadata.StartedAt ?? nativeStartedAt,
                    metadata.MetadataStatus);
                FileUsageOwnerKind kind = info.ApplicationType == RestartManagerApplicationType.Service
                    ? FileUsageOwnerKind.Service
                    : info.ApplicationType == RestartManagerApplicationType.Unknown
                        ? FileUsageOwnerKind.Unknown
                        : FileUsageOwnerKind.Application;
                owners.Add(new(process, kind, NullIfEmpty(info.ServiceShortName), info.Restartable,
                    MetadataReason(metadata.MetadataStatus)));
            }
            result = Snapshot(path, owners, probes);
        }
        catch (OperationCanceledException)
        {
            result = Failure(path, FileUsageErrorKind.Cancelled, "File usage inspection was cancelled.", probes: probes);
        }
        catch (RestartManagerException ex)
        {
            result = Failure(path, MapError(ex.NativeErrorCode), ex.Message, ex.NativeErrorCode, probes);
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException)
        {
            int? code = ex is Win32Exception win32 ? win32.NativeErrorCode : null;
            result = Failure(path, ex is UnauthorizedAccessException or Win32Exception { NativeErrorCode: 5 }
                ? FileUsageErrorKind.AccessDenied : FileUsageErrorKind.PlatformError, ex.Message, code, probes);
        }
        finally
        {
            if (started)
            {
                int endError = _native.EndSession(session);
                if (endError != ErrorSuccess)
                    result = WithError(result, path, probes, MapError(endError), NativeMessage("end the Restart Manager session", endError), endError);
            }
        }
        return result ?? Failure(path, FileUsageErrorKind.PlatformError, "Restart Manager did not return a result.");
    }

    public FileUsageReleaseResult Release(FileUsageReleaseRequest request, CancellationToken cancellationToken = default)
    {
        if (request.OwnerKind == FileUsageOwnerKind.Service)
            return new(FileUsageReleaseStatus.IneligibleOwner, "Windows service owners cannot be released.");
        if (!_termination.Support.CanTerminate)
            return new(FileUsageReleaseStatus.NotSupported, Support.ReleaseUnavailableReason);

        ProcessTerminationResult result = _termination.TerminateProcess(request.Identity, cancellationToken);
        return new(result.Status switch
        {
            ProcessTerminationStatus.Success => FileUsageReleaseStatus.Success,
            ProcessTerminationStatus.NotFound or ProcessTerminationStatus.AlreadyExited => FileUsageReleaseStatus.AlreadyExited,
            ProcessTerminationStatus.AccessDenied => FileUsageReleaseStatus.AccessDenied,
            ProcessTerminationStatus.StaleIdentity => FileUsageReleaseStatus.StaleIdentity,
            ProcessTerminationStatus.CurrentProcess => FileUsageReleaseStatus.CurrentProcess,
            ProcessTerminationStatus.NotSupported => FileUsageReleaseStatus.NotSupported,
            _ => FileUsageReleaseStatus.Failed
        }, result.Message);
    }

    private IReadOnlyList<FileUsageProbe> Probe(string path) =>
    [
        Probe(path, FileUsageOperation.Read, FileAccessNative.GenericRead),
        Probe(path, FileUsageOperation.Write, FileAccessNative.GenericWrite),
        Probe(path, FileUsageOperation.Delete, FileAccessNative.Delete),
        Probe(path, FileUsageOperation.Rename, FileAccessNative.Delete)
    ];

    private FileUsageProbe Probe(string path, FileUsageOperation operation, uint access)
    {
        int error = _fileAccess.TryOpen(path, access,
            FileAccessNative.ShareRead | FileAccessNative.ShareWrite | FileAccessNative.ShareDelete);
        if (error == ErrorSuccess)
            return new(operation, FileUsageProbeStatus.Allowed);

        FileUsageProbeStatus status = error is ErrorAccessDenied or ErrorSharingViolation
            ? FileUsageProbeStatus.Blocked : FileUsageProbeStatus.Unknown;
        return new(operation, status, new(MapError(error),
            NativeMessage($"probe {operation.ToString().ToLowerInvariant()} access", error), error));
    }

    private IReadOnlyList<RestartManagerProcessInfo> GetOwners(uint session, CancellationToken token)
    {
        uint capacity = 0;
        for (int attempt = 0; attempt < MaxResizeAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            uint needed;
            uint count = capacity;
            RestartManagerProcessInfo[]? buffer = capacity == 0 ? null : new RestartManagerProcessInfo[capacity];
            uint reasons = 0;
            int error = _native.GetList(session, out needed, ref count, buffer, ref reasons);
            if (error == ErrorSuccess)
                return buffer is null || count == 0 ? [] : buffer.Take(checked((int)count)).ToArray();
            if (error != ErrorMoreData)
                Check(error, "read the Restart Manager owner list");
            if (needed <= capacity)
                throw new RestartManagerException(error, "Restart Manager did not provide a larger owner buffer.");
            capacity = needed;
        }
        throw new RestartManagerException(ErrorMoreData, "The Restart Manager owner list changed too frequently to capture.");
    }

    private static void Check(int error, string operation)
    {
        if (error != ErrorSuccess) throw new RestartManagerException(error, NativeMessage(operation, error));
    }

    private static string NativeMessage(string operation, int error) =>
        $"Unable to {operation}: {new Win32Exception(error).Message}";
    private static FileUsageErrorKind MapError(int error) => error switch
    {
        2 or 3 => FileUsageErrorKind.NotFound,
        5 => FileUsageErrorKind.AccessDenied,
        87 or 160 => FileUsageErrorKind.InvalidPath,
        _ => FileUsageErrorKind.PlatformError
    };
    private static FileUsageSnapshot Snapshot(string path, IReadOnlyList<FileUsageOwnerEntry> owners,
        IReadOnlyList<FileUsageProbe> probes, FileUsageError? error = null)
    {
        FileUsageState state = probes.Any(p => p.Status == FileUsageProbeStatus.Blocked)
            ? FileUsageState.Blocked
            : error is null && probes.All(p => p.Status == FileUsageProbeStatus.Allowed)
                ? owners.Count == 0 ? FileUsageState.Free : FileUsageState.InUse
                : FileUsageState.Unavailable;
        return new(path, DateTimeOffset.Now, state, owners, probes, error);
    }
    private static FileUsageSnapshot Failure(string path, FileUsageErrorKind kind, string message,
        int? code = null, IReadOnlyList<FileUsageProbe>? probes = null) =>
        Snapshot(path, [], probes ?? [], new(kind, message, code));
    private static FileUsageSnapshot WithError(FileUsageSnapshot? result, string path,
        IReadOnlyList<FileUsageProbe> probes, FileUsageErrorKind kind, string message, int code) =>
        result is null ? Failure(path, kind, message, code, probes)
            : Snapshot(path, result.Owners, result.Probes, new(kind, message, code));
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string? MetadataReason(ProcessMetadataStatus status) => status switch
    {
        ProcessMetadataStatus.Available => null,
        ProcessMetadataStatus.Partial => "Some process metadata is unavailable.",
        ProcessMetadataStatus.AccessDenied => "Access to process metadata was denied.",
        ProcessMetadataStatus.Exited => "The process exited while metadata was read.",
        _ => "Process metadata is unavailable."
    };
    private static DateTimeOffset? FromFileTime(long value)
    {
        if (value <= 0) return null;
        try { return DateTimeOffset.FromFileTime(value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}

internal interface IProcessSnapshotReader { ProcessSnapshot Read(int processId); }

internal sealed class ProcessSnapshotReader : IProcessSnapshotReader
{
    public ProcessSnapshot Read(int processId) => WindowsProcessesAndPortsPlatformService.ReadProcess(processId);
}

internal sealed class RestartManagerException(int nativeErrorCode, string message) : Exception(message)
{
    public int NativeErrorCode { get; } = nativeErrorCode;
}

internal enum RestartManagerApplicationType { Unknown, MainWindow, OtherWindow, Service, Explorer, Console, Critical }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct RestartManagerProcessInfo
{
    public int ProcessId;
    public long ProcessStartTime;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ApplicationName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceShortName;
    public RestartManagerApplicationType ApplicationType;
    public uint ApplicationStatus;
    public uint TerminalSessionId;
    [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
}

internal interface IRestartManagerNative
{
    int StartSession(out uint sessionHandle, string sessionKey);
    int RegisterResources(uint sessionHandle, string[] fileNames);
    int GetList(uint sessionHandle, out uint needed, ref uint count, RestartManagerProcessInfo[]? processes, ref uint rebootReasons);
    int EndSession(uint sessionHandle);
}

internal sealed class RestartManagerNative : IRestartManagerNative
{
    public int StartSession(out uint sessionHandle, string sessionKey) => RmStartSession(out sessionHandle, 0, sessionKey);
    public int RegisterResources(uint sessionHandle, string[] fileNames) => RmRegisterResources(sessionHandle, (uint)fileNames.Length, fileNames, 0, IntPtr.Zero, 0, null);
    public int GetList(uint sessionHandle, out uint needed, ref uint count, RestartManagerProcessInfo[]? processes, ref uint rebootReasons) => RmGetList(sessionHandle, out needed, ref count, processes, ref rebootReasons);
    public int EndSession(uint sessionHandle) => RmEndSession(sessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, string sessionKey);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint sessionHandle, uint fileCount, string[] fileNames, uint applicationCount, IntPtr applications, uint serviceCount, string[]? serviceNames);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmGetList(uint sessionHandle, out uint needed, ref uint count, [In, Out] RestartManagerProcessInfo[]? processes, ref uint rebootReasons);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint sessionHandle);
}

internal interface IFileAccessNative
{
    int TryOpen(string path, uint desiredAccess, uint shareMode);
}

internal sealed class FileAccessNative : IFileAccessNative
{
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint Delete = 0x00010000;
    internal const uint ShareRead = 0x00000001;
    internal const uint ShareWrite = 0x00000002;
    internal const uint ShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    public int TryOpen(string path, uint desiredAccess, uint shareMode)
    {
        using SafeFileHandle handle = CreateFile(path, desiredAccess, shareMode, IntPtr.Zero,
            OpenExisting, FileAttributeNormal, IntPtr.Zero);
        return handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
