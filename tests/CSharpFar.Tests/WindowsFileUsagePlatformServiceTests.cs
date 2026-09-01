using CSharpFar.Platform.Abstractions;
using CSharpFar.Platform.Windows;

namespace CSharpFar.Tests;

public sealed class WindowsFileUsagePlatformServiceTests
{
    [Fact]
    public void Inspect_EndsSession_WhenRegistrationFails()
    {
        var native = new FakeNative { RegisterError = 5 };
        FileUsageSnapshot result = Service(native).Inspect("locked.txt");

        Assert.Equal(FileUsageErrorKind.AccessDenied, result.Error?.Kind);
        Assert.Equal(1, native.EndCalls);
    }

    [Fact]
    public void Inspect_ReturnsFreeForNoOwners()
    {
        var native = new FakeNative();
        FileUsageSnapshot result = Service(native).Inspect("free.txt");

        Assert.Equal(FileUsageState.Free, result.State);
        Assert.Empty(result.Owners);
        Assert.Equal(4, result.Probes.Count);
        Assert.All(result.Probes, probe => Assert.Equal(FileUsageProbeStatus.Allowed, probe.Status));
        Assert.Equal(1, native.EndCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Inspect_MapsOneOrMultipleOwners(int count)
    {
        var native = new FakeNative { Owners = Enumerable.Range(1, count).Select(Owner).ToArray() };
        FileUsageSnapshot result = Service(native).Inspect("busy.txt");

        Assert.Equal(FileUsageState.InUse, result.State);
        Assert.Equal(count, result.Owners.Count);
        Assert.All(result.Owners, owner => Assert.Equal(FileUsageOwnerKind.Application, owner.Kind));
        Assert.Equal(1, native.EndCalls);
    }

    [Fact]
    public void Inspect_ResizesOwnerBufferAfterMoreData()
    {
        var native = new FakeNative { Owners = [Owner(10), Owner(20)] };
        FileUsageSnapshot result = Service(native).Inspect("busy.txt");

        Assert.Equal(2, result.Owners.Count);
        Assert.Equal([0u, 2u], native.RequestedCapacities);
    }

    [Fact]
    public void Inspect_CancellationBetweenNativeCallsStillEndsSession()
    {
        using var cancellation = new CancellationTokenSource();
        var native = new FakeNative { AfterRegister = cancellation.Cancel };
        FileUsageSnapshot result = Service(native).Inspect("busy.txt", cancellation.Token);

        Assert.Equal(FileUsageErrorKind.Cancelled, result.Error?.Kind);
        Assert.Equal(0, native.GetListCalls);
        Assert.Equal(1, native.EndCalls);
    }

    [Fact]
    public void Inspect_MapsGetListNativeError()
    {
        var native = new FakeNative { GetListError = 87 };
        FileUsageSnapshot result = Service(native).Inspect("bad.txt");

        Assert.Equal(FileUsageErrorKind.InvalidPath, result.Error?.Kind);
        Assert.Equal(87, result.Error?.PlatformErrorCode);
        Assert.Equal(1, native.EndCalls);
    }

    [Fact]
    public void Inspect_PreservesOwnerAndRestartManagerDetailsWhenMetadataIsDenied()
    {
        RestartManagerProcessInfo info = Owner(42);
        info.ApplicationName = "Native application";
        info.ServiceShortName = "svc-short";
        info.ApplicationType = RestartManagerApplicationType.Service;
        info.Restartable = true;
        info.ProcessStartTime = DateTimeOffset.Parse("2026-01-02T03:04:05Z").ToFileTime();
        var native = new FakeNative { Owners = [info] };
        var metadata = new FakeProcessReader(new(42, null, null, null, ProcessMetadataStatus.AccessDenied));

        FileUsageOwnerEntry owner = Service(native, metadata).Inspect("busy.txt").Owners.Single();

        Assert.Equal("Native application", owner.Process.Name);
        Assert.NotNull(owner.Process.StartedAt);
        Assert.Equal(ProcessMetadataStatus.AccessDenied, owner.Process.MetadataStatus);
        Assert.Equal(FileUsageOwnerKind.Service, owner.Kind);
        Assert.Equal("svc-short", owner.ServiceName);
        Assert.True(owner.IsRestartable);
        Assert.NotNull(owner.MetadataUnavailableReason);
    }

    [Fact]
    public void Inspect_ClassifiesSharingViolationAsBlockedAndOtherNativeFailureAsUnknown()
    {
        var access = new FakeFileAccess(new Dictionary<uint, int>
        {
            [FileAccessNative.GenericRead] = 0,
            [FileAccessNative.GenericWrite] = 32,
            [FileAccessNative.Delete] = 123
        });

        FileUsageSnapshot result = Service(new FakeNative(), fileAccess: access).Inspect("file.txt");

        Assert.Equal(FileUsageState.Blocked, result.State);
        Assert.Equal(FileUsageProbeStatus.Allowed, result.Probes.Single(p => p.Operation == FileUsageOperation.Read).Status);
        Assert.Equal(FileUsageProbeStatus.Blocked, result.Probes.Single(p => p.Operation == FileUsageOperation.Write).Status);
        Assert.Equal(32, result.Probes.Single(p => p.Operation == FileUsageOperation.Write).Error?.PlatformErrorCode);
        Assert.Equal(FileUsageProbeStatus.Unknown, result.Probes.Single(p => p.Operation == FileUsageOperation.Delete).Status);
        Assert.Equal(123, result.Probes.Single(p => p.Operation == FileUsageOperation.Delete).Error?.PlatformErrorCode);
    }

    [Fact]
    public void Inspect_ClassifiesAccessDeniedAsBlocked()
    {
        var access = new FakeFileAccess(new Dictionary<uint, int> { [FileAccessNative.GenericRead] = 5 });

        FileUsageProbe probe = Service(new FakeNative(), fileAccess: access).Inspect("file.txt").Probes
            .Single(p => p.Operation == FileUsageOperation.Read);

        Assert.Equal(FileUsageProbeStatus.Blocked, probe.Status);
        Assert.Equal(FileUsageErrorKind.AccessDenied, probe.Error?.Kind);
        Assert.Equal(5, probe.Error?.PlatformErrorCode);
    }

    [Fact]
    public void Inspect_PreservesBlockedProbeWhenRestartManagerFails()
    {
        var access = new FakeFileAccess(new Dictionary<uint, int> { [FileAccessNative.GenericWrite] = 32 });

        FileUsageSnapshot result = Service(new FakeNative { RegisterError = 5 }, fileAccess: access).Inspect("file.txt");

        Assert.Equal(FileUsageState.Blocked, result.State);
        Assert.Equal(FileUsageErrorKind.AccessDenied, result.Error?.Kind);
        Assert.Contains(result.Probes, p => p.Operation == FileUsageOperation.Write && p.Status == FileUsageProbeStatus.Blocked);
    }

    [Theory]
    [InlineData(ProcessTerminationStatus.Success, FileUsageReleaseStatus.Success)]
    [InlineData(ProcessTerminationStatus.CurrentProcess, FileUsageReleaseStatus.CurrentProcess)]
    [InlineData(ProcessTerminationStatus.StaleIdentity, FileUsageReleaseStatus.StaleIdentity)]
    [InlineData(ProcessTerminationStatus.NotFound, FileUsageReleaseStatus.AlreadyExited)]
    [InlineData(ProcessTerminationStatus.AlreadyExited, FileUsageReleaseStatus.AlreadyExited)]
    [InlineData(ProcessTerminationStatus.AccessDenied, FileUsageReleaseStatus.AccessDenied)]
    [InlineData(ProcessTerminationStatus.NotSupported, FileUsageReleaseStatus.NotSupported)]
    [InlineData(ProcessTerminationStatus.Failed, FileUsageReleaseStatus.Failed)]
    public void Release_MapsTerminationOutcomeAndPreservesMessage(ProcessTerminationStatus terminationStatus,
        FileUsageReleaseStatus expectedStatus)
    {
        var termination = new FakeTermination(terminationStatus, "termination message");
        var identity = new ProcessIdentity(42, DateTimeOffset.UnixEpoch);
        WindowsFileUsagePlatformService service = Service(new FakeNative(), termination: termination);

        FileUsageReleaseResult result = service.Release(new(identity));

        Assert.True(service.Support.CanReleaseOwners);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal("termination message", result.Message);
        Assert.Equal(identity, termination.LastIdentity);
    }

    [Fact]
    public void Release_DoesNotDelegateForServiceOwner()
    {
        var termination = new FakeTermination(ProcessTerminationStatus.Success);

        FileUsageReleaseResult result = Service(new FakeNative(), termination: termination).Release(
            new(new ProcessIdentity(42, DateTimeOffset.UnixEpoch), FileUsageOwnerKind.Service));

        Assert.Equal(FileUsageReleaseStatus.IneligibleOwner, result.Status);
        Assert.Null(termination.LastIdentity);
    }

    [Fact]
    public void Release_IsUnsupportedWhenTerminationCapabilityIsUnavailable()
    {
        var termination = new FakeTermination(ProcessTerminationStatus.Success, canTerminate: false,
            unavailableReason: "disabled here");
        WindowsFileUsagePlatformService service = Service(new FakeNative(), termination: termination);

        FileUsageReleaseResult result = service.Release(new(new ProcessIdentity(42, DateTimeOffset.UnixEpoch)));

        Assert.False(service.Support.CanReleaseOwners);
        Assert.Equal(FileUsageReleaseStatus.NotSupported, result.Status);
        Assert.Equal("disabled here", result.Message);
        Assert.Null(termination.LastIdentity);
    }

    [Fact]
    public void Release_PreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var termination = new FakeTermination(ProcessTerminationStatus.Success);

        Assert.Throws<OperationCanceledException>(() => Service(new FakeNative(), termination: termination)
            .Release(new(new ProcessIdentity(42, DateTimeOffset.UnixEpoch)), cancellation.Token));
    }

    [Fact]
    public void NativeProbes_UnrestrictedHandleAllowsNonMutatingAccessChecks()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.GetTempFileName();
        try
        {
            using Microsoft.Win32.SafeHandles.SafeFileHandle held = File.OpenHandle(path, FileMode.Open,
                FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

            FileUsageSnapshot result = Service(new FakeNative(), fileAccess: new FileAccessNative()).Inspect(path);

            Assert.Equal(FileUsageProbeStatus.Allowed, Probe(result, FileUsageOperation.Read).Status);
            Assert.Equal(FileUsageProbeStatus.Allowed, Probe(result, FileUsageOperation.Write).Status);
            Assert.Equal(FileUsageProbeStatus.Allowed, Probe(result, FileUsageOperation.Delete).Status);
            Assert.Equal(FileUsageProbeStatus.Allowed, Probe(result, FileUsageOperation.Rename).Status);
            Assert.Equal(FileUsageState.Free, result.State);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NativeProbes_ReadOnlySharingBlocksWriteDeleteAndRename()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.GetTempFileName();
        try
        {
            using Microsoft.Win32.SafeHandles.SafeFileHandle held = File.OpenHandle(path, FileMode.Open,
                FileAccess.Read, FileShare.Read);

            FileUsageSnapshot result = Service(new FakeNative(), fileAccess: new FileAccessNative()).Inspect(path);

            Assert.Equal(FileUsageProbeStatus.Allowed, Probe(result, FileUsageOperation.Read).Status);
            AssertBlockedBySharingViolation(result, FileUsageOperation.Write);
            AssertBlockedBySharingViolation(result, FileUsageOperation.Delete);
            AssertBlockedBySharingViolation(result, FileUsageOperation.Rename);
            Assert.Equal(FileUsageState.Blocked, result.State);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NativeProbes_ExclusiveHandleBlocksEveryOperation()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.GetTempFileName();
        try
        {
            using Microsoft.Win32.SafeHandles.SafeFileHandle held = File.OpenHandle(path, FileMode.Open,
                FileAccess.ReadWrite, FileShare.None);

            FileUsageSnapshot result = Service(new FakeNative(), fileAccess: new FileAccessNative()).Inspect(path);

            Assert.All(result.Probes, probe =>
            {
                Assert.Equal(FileUsageProbeStatus.Blocked, probe.Status);
                Assert.Equal(32, probe.Error?.PlatformErrorCode);
            });
            Assert.Equal(FileUsageState.Blocked, result.State);
        }
        finally { File.Delete(path); }
    }

    private static FileUsageProbe Probe(FileUsageSnapshot snapshot, FileUsageOperation operation) =>
        snapshot.Probes.Single(p => p.Operation == operation);

    private static void AssertBlockedBySharingViolation(FileUsageSnapshot snapshot, FileUsageOperation operation)
    {
        FileUsageProbe probe = Probe(snapshot, operation);
        Assert.Equal(FileUsageProbeStatus.Blocked, probe.Status);
        Assert.Equal(32, probe.Error?.PlatformErrorCode);
    }

    private static WindowsFileUsagePlatformService Service(FakeNative native, IProcessSnapshotReader? reader = null,
        IFileAccessNative? fileAccess = null, IProcessesAndPortsPlatformService? termination = null) =>
        new(native, reader ?? new FakeProcessReader(), fileAccess ?? new FakeFileAccess(), termination);

    private static RestartManagerProcessInfo Owner(int pid) => new()
    {
        ProcessId = pid,
        ApplicationName = $"app-{pid}",
        ServiceShortName = string.Empty,
        ApplicationType = RestartManagerApplicationType.MainWindow
    };

    private sealed class FakeProcessReader(ProcessSnapshot? snapshot = null) : IProcessSnapshotReader
    {
        public ProcessSnapshot Read(int processId) => snapshot ?? new(processId, $"process-{processId}", $"C:/{processId}.exe", DateTimeOffset.UnixEpoch);
    }

    private sealed class FakeFileAccess(IReadOnlyDictionary<uint, int>? errors = null) : IFileAccessNative
    {
        public int TryOpen(string path, uint desiredAccess, uint shareMode) =>
            errors is not null && errors.TryGetValue(desiredAccess, out int error) ? error : 0;
    }

    private sealed class FakeTermination(ProcessTerminationStatus status, string? message = null,
        bool canTerminate = true, string? unavailableReason = null) : IProcessesAndPortsPlatformService
    {
        public ProcessesAndPortsSupportInfo Support { get; } = new(true, canTerminate,
            TerminationUnavailableReason: unavailableReason);
        public ProcessIdentity? LastIdentity { get; private set; }
        public ProcessesAndPortsSnapshot CaptureSnapshot(ProcessesAndPortsQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProcessTerminationResult TerminateProcess(ProcessIdentity identity,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastIdentity = identity;
            return new(status, message);
        }
    }

    private sealed class FakeNative : IRestartManagerNative
    {
        public RestartManagerProcessInfo[] Owners { get; init; } = [];
        public int RegisterError { get; init; }
        public int GetListError { get; init; }
        public Action? AfterRegister { get; init; }
        public int EndCalls { get; private set; }
        public int GetListCalls { get; private set; }
        public List<uint> RequestedCapacities { get; } = [];

        public int StartSession(out uint sessionHandle, string sessionKey) { sessionHandle = 123; return 0; }
        public int RegisterResources(uint sessionHandle, string[] fileNames)
        {
            AfterRegister?.Invoke();
            return RegisterError;
        }
        public int GetList(uint sessionHandle, out uint needed, ref uint count, RestartManagerProcessInfo[]? processes, ref uint rebootReasons)
        {
            GetListCalls++;
            RequestedCapacities.Add(count);
            if (GetListError != 0) { needed = 0; return GetListError; }
            needed = (uint)Owners.Length;
            if (processes is null || count < needed) return Owners.Length == 0 ? 0 : 234;
            Array.Copy(Owners, processes, Owners.Length);
            count = needed;
            return 0;
        }
        public int EndSession(uint sessionHandle) { EndCalls++; return 0; }
    }
}
