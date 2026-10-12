using CSharpFar.App.Commands;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;
using CSharpFar.Platform.MacOs;

namespace CSharpFar.Tests;

public sealed class MacOsDriveCatalogTests
{
    [Fact]
    public void NativeSnapshot_KeepsAllMounts_ClassifiesFromFlags_AndDoesNotMergeEqualNames()
    {
        var service = new MacOsVolumeService(new StubProvider(
        [
            new MacOsMount("/", "apfs", false),
            new MacOsMount("/System/Volumes/Preboot", "apfs", true),
            new MacOsMount("/Volumes/Work", "apfs", false, 1000, 400, "Work"),
            new MacOsMount("/Volumes/Work2", "apfs", false, 1000, 200, "Work"),
            new MacOsMount("/mnt/offline", "smbfs", false),
            new MacOsMount("/dev", "devfs", true),
            new MacOsMount("/Volumes/Work/", "apfs", false),
        ]));

        var volumes = service.GetVolumes();
        Assert.Equal(6, volumes.Count);
        Assert.Equal("/", volumes[0].RootPath);
        Assert.Equal(VolumeVisibility.User, volumes.Single(x => x.RootPath == "/").Visibility);
        Assert.Equal(VolumeVisibility.System, volumes.Single(x => x.RootPath == "/System/Volumes/Preboot").Visibility);
        Assert.Equal(VolumeVisibility.System, volumes.Single(x => x.RootPath == "/dev").Visibility);
        Assert.Equal(VolumeVisibility.User, volumes.Single(x => x.RootPath == "/Volumes/Work2").Visibility);
        Assert.Equal(VolumeKind.Network, volumes.Single(x => x.RootPath == "/mnt/offline").Kind);
        Assert.Equal(VolumeStatus.Unchecked, volumes.Single(x => x.RootPath == "/mnt/offline").Status);
        Assert.Null(volumes.Single(x => x.RootPath == "/mnt/offline").TotalBytes);
        Assert.Equal(400, volumes.Single(x => x.RootPath == "/Volumes/Work").FreeBytes);
    }

    [Fact]
    public void NativeSnapshot_PreservesCaseDistinctMountsAndAddsMissingRoot()
    {
        var volumes = MacOsVolumeService.BuildNative(
        [
            new MacOsMount("/Volumes/Work", "apfs", false),
            new MacOsMount("/Volumes/work", "apfs", true),
        ]);

        Assert.Equal(3, volumes.Count);
        Assert.Equal(VolumeVisibility.System, volumes.Single(x => x.RootPath == "/Volumes/work").Visibility);
        Assert.Equal(VolumeVisibility.User, volumes.Single(x => x.RootPath == "/").Visibility);
    }

    [Fact]
    public void SharedSnapshot_IsCachedAndIncludesTechnicalMountPoints()
    {
        var provider = new StubProvider([new MacOsMount("/", "apfs", false), new MacOsMount("/dev", "devfs", true)]);
        var service = new MacOsVolumeService(provider);
        var first = service.GetVolumes();
        var second = service.GetVolumes();

        Assert.Same(first, second);
        Assert.Equal(1, provider.Calls);
        Assert.Contains(second, x => x.RootPath == "/dev");
        service.Invalidate();
        Assert.NotSame(first, service.GetVolumes());
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public void MostSpecificMountOwnership_RespectsBoundariesAndCase()
    {
        FileSystemVolume[] mounts =
        [
            Volume("/"), Volume("/Volumes/Work"), Volume("/Volumes/Work2"),
        ];
        Assert.True(DriveSelectionCommand.ContainsPath("/Volumes/Work2", "/Volumes/Work2/folder"));
        Assert.False(DriveSelectionCommand.ContainsPath("/Volumes/Work", "/Volumes/Work2/folder"));
        Assert.Equal("/",
            DriveSelectionCommand.SelectedVolumeDirectory("/", "/Volumes/Work2/folder", PanelSourceId.Local, mounts));
        Assert.Equal("/Volumes/Work2/folder",
            DriveSelectionCommand.SelectedVolumeDirectory("/Volumes/Work2", "/Volumes/Work2/folder", PanelSourceId.Local, mounts));
        Assert.Equal("/Volumes/Work2",
            DriveSelectionCommand.SelectedVolumeDirectory("/Volumes/Work2", "/Volumes/Work2/folder", PanelSourceId.Module(Guid.NewGuid(), "sftp"), mounts));
        if (!OperatingSystem.IsWindows())
            Assert.False(DriveSelectionCommand.ContainsPath("/Volumes/Work", "/Volumes/work/file"));
    }

    [Fact]
    public void NativeInteropSmoke_MacOSOnly()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var mounts = new MacOsNativeMountProvider().GetMounts();
        Assert.Contains(mounts, x => x.RootPath == "/");
    }

    private static FileSystemVolume Volume(string root) => new()
    {
        Id = root, RootPath = root, DisplayName = root, Status = VolumeStatus.Ready,
    };

    private sealed class StubProvider(IReadOnlyList<MacOsMount> mounts) : IMacOsMountProvider
    {
        public int Calls { get; private set; }
        public IReadOnlyList<MacOsMount> GetMounts() { Calls++; return mounts; }
    }
}
