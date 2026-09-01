using CSharpFar.Platform.Abstractions;

namespace CSharpFar.Tests;

public sealed class FileUsageContractTests
{
    [Fact]
    public void OwnerEntry_ReusesProcessSnapshotAndPidReuseSafeIdentity()
    {
        var startedAt = DateTimeOffset.Parse("2026-08-01T10:00:00Z");
        var process = new ProcessSnapshot(42, "editor", null, startedAt, ProcessMetadataStatus.Partial);
        var owner = new FileUsageOwnerEntry(process, FileUsageOwnerKind.Application, MetadataUnavailableReason: "Executable path denied");

        Assert.Same(process, owner.Process);
        Assert.Equal(new ProcessIdentity(42, startedAt), owner.Process.Identity);
        Assert.Equal(ProcessMetadataStatus.Partial, owner.Process.MetadataStatus);
    }

    [Fact]
    public void Snapshot_RepresentsIndependentProbeResultsAndPartialFailure()
    {
        var probes = new[]
        {
            new FileUsageProbe(FileUsageOperation.Read, FileUsageProbeStatus.Allowed),
            new FileUsageProbe(FileUsageOperation.Write, FileUsageProbeStatus.Blocked),
            new FileUsageProbe(FileUsageOperation.Delete, FileUsageProbeStatus.Unknown,
                new FileUsageError(FileUsageErrorKind.AccessDenied, "Probe denied"))
        };

        var snapshot = new FileUsageSnapshot("C:/file.txt", DateTimeOffset.UtcNow, FileUsageState.Blocked, [], probes);

        Assert.Equal(new[] { FileUsageProbeStatus.Allowed, FileUsageProbeStatus.Blocked, FileUsageProbeStatus.Unknown }, snapshot.Probes.Select(x => x.Status));
    }

    [Fact]
    public void UnsupportedService_ReturnsExplicitUnavailableResults()
    {
        var service = new UnsupportedFileUsagePlatformService("Unavailable here");
        var identity = new ProcessIdentity(7, DateTimeOffset.UtcNow);

        FileUsageSnapshot snapshot = service.Inspect("/tmp/file");
        FileUsageReleaseResult release = service.Release(new FileUsageReleaseRequest(identity));

        Assert.False(service.Support.IsSupported);
        Assert.Equal(FileUsageState.Unavailable, snapshot.State);
        Assert.Equal(FileUsageErrorKind.Unsupported, snapshot.Error?.Kind);
        Assert.Equal("Unavailable here", snapshot.Error?.Message);
        Assert.Equal(FileUsageReleaseStatus.NotSupported, release.Status);
    }
}
