using CSharpFar.FileSystem;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class UnixMountInfoReaderTests
{
    [Fact]
    public void Parse_ReadsRootMount()
    {
        var entries = UnixMountInfoReader.Parse([
            "36 25 8:1 / / rw,relatime - ext4 /dev/sda1 rw"
        ]);

        Assert.Single(entries);
        Assert.Equal("/", entries[0].MountPoint);
        Assert.Equal("/dev/sda1", entries[0].Source);
        Assert.Equal("ext4", entries[0].FileSystemType);
    }

    [Fact]
    public void Parse_UnescapesMountPointSpace()
    {
        var entries = UnixMountInfoReader.Parse([
            "37 25 8:2 / /media/My\\040Drive rw,relatime - ext4 /dev/sdb1 rw"
        ]);

        Assert.Equal("/media/My Drive", entries[0].MountPoint);
    }

    [Fact]
    public void IsUserVisible_IncludesWslAndNetworkMounts()
    {
        Assert.True(UnixMountInfoReader.IsUserVisible(new UnixMountInfoEntry("C:\\", "/mnt/c", "drvfs")));
        Assert.True(UnixMountInfoReader.IsUserVisible(new UnixMountInfoEntry("//server/share", "/mnt/share", "cifs")));
    }

    [Fact]
    public void IsUserVisible_FiltersTechnicalMounts()
    {
        Assert.False(UnixMountInfoReader.IsUserVisible(new UnixMountInfoEntry("proc", "/proc", "proc")));
        Assert.False(UnixMountInfoReader.IsUserVisible(new UnixMountInfoEntry("sysfs", "/sys", "sysfs")));
    }
}
