using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class LinuxMountInfoReaderTests
{
    [Fact]
    public void Parse_ReadsRootMount()
    {
        var entries = LinuxMountInfoReader.Parse([
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
        var entries = LinuxMountInfoReader.Parse([
            "37 25 8:2 / /media/My\\040Drive rw,relatime - ext4 /dev/sdb1 rw"
        ]);

        Assert.Equal("/media/My Drive", entries[0].MountPoint);
    }

    [Fact]
    public void IsUserVisible_IncludesWslAndNetworkMounts()
    {
        Assert.True(LinuxMountInfoReader.IsUserVisible(new LinuxMountInfoEntry("C:\\", "/mnt/c", "drvfs")));
        Assert.True(LinuxMountInfoReader.IsUserVisible(new LinuxMountInfoEntry("//server/share", "/mnt/share", "cifs")));
    }

    [Fact]
    public void IsUserVisible_FiltersTechnicalMounts()
    {
        Assert.False(LinuxMountInfoReader.IsUserVisible(new LinuxMountInfoEntry("proc", "/proc", "proc")));
        Assert.False(LinuxMountInfoReader.IsUserVisible(new LinuxMountInfoEntry("sysfs", "/sys", "sysfs")));
    }
}
