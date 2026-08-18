using CSharpFar.FileSystem;
using CSharpFar.Platform.Linux;
using CSharpFar.Platform.MacOs;
using CSharpFar.Platform.Windows;
using CSharpFar.Shell;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class PlatformCompositionTests
{
    [Fact]
    public void WindowsPlatformServices_UsesWindowsImplementations()
    {
        var driver = new FakeConsoleDriver();
        using var platform = new WindowsPlatformServices(
            driver,
            driver,
            new ShellService(),
            new WindowsShellFileLauncher(new WindowsExecutableFileDetector()),
            new DpapiCredentialStore(Path.GetTempPath()),
            new WindowsVolumeService(),
            new VolumeInfoService(),
            new FileSystemLocationService(),
            new VolumeMountPointService(),
            new WindowsFileSystemPlatformOperations());

        Assert.IsType<WindowsShellFileLauncher>(platform.FileLauncher);
        Assert.IsType<DpapiCredentialStore>(platform.CredentialStore);
        Assert.IsType<WindowsVolumeService>(platform.VolumeService);
        Assert.IsType<WindowsFileSystemPlatformOperations>(platform.FileSystemOperations);
    }

    [Fact]
    public void LinuxPlatformServices_DoesNotUseWindowsOnlyImplementations()
    {
        var driver = new FakeConsoleDriver();
        using var platform = new LinuxPlatformServices(
            driver,
            driver,
            new ShellService("/bin/sh", "-c \"{0}\""),
            new UnixShellFileLauncher(new UnixExecutableFileDetector()),
            new FileCredentialStore(Path.GetTempPath()),
            new LinuxVolumeService(),
            new VolumeInfoService(),
            new FileSystemLocationService(),
            new LinuxVolumeMountPointService(),
            new LinuxFileSystemPlatformOperations());

        Assert.IsNotType<WindowsShellFileLauncher>(platform.FileLauncher);
        Assert.IsNotType<DpapiCredentialStore>(platform.CredentialStore);
        Assert.IsNotType<WindowsVolumeService>(platform.VolumeService);
        Assert.IsType<UnixShellFileLauncher>(platform.FileLauncher);
        Assert.IsType<FileCredentialStore>(platform.CredentialStore);
        Assert.IsType<LinuxVolumeService>(platform.VolumeService);
        Assert.IsType<LinuxFileSystemPlatformOperations>(platform.FileSystemOperations);
    }

    [Fact]
    public void MacOsPlatformServices_DoesNotUseLinuxOrWindowsVolumeServices()
    {
        var driver = new FakeConsoleDriver();
        using var platform = new MacOsPlatformServices(
            driver, driver, new ShellService("/bin/sh", "-c \"{0}\""),
            new UnixShellFileLauncher(new UnixExecutableFileDetector(), new MacOsAssociationLauncher()),
            new FileCredentialStore(Path.GetTempPath()), new MacOsVolumeService(), new VolumeInfoService(),
            new FileSystemLocationService(), new MacOsVolumeMountPointService(), new MacOsFileSystemPlatformOperations());

        Assert.IsType<MacOsVolumeService>(platform.VolumeService);
        Assert.IsType<MacOsFileSystemPlatformOperations>(platform.FileSystemOperations);
        Assert.IsNotType<LinuxVolumeService>(platform.VolumeService);
        Assert.IsNotType<WindowsVolumeService>(platform.VolumeService);
    }
}
