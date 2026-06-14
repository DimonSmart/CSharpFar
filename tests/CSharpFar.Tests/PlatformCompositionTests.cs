using System.Reflection;
using CSharpFar.App.Bootstrap;
using CSharpFar.FileSystem;
using CSharpFar.Platform.Unix;
using CSharpFar.Platform.Windows;
using CSharpFar.Shell;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class PlatformCompositionTests
{
    [Fact]
    public void ApplicationBootstrap_DoesNotConstructPlatformSpecificServices()
    {
        var bootstrapAssembly = typeof(ApplicationBootstrap).Assembly;
        var bootstrapTypes = bootstrapAssembly.GetTypes()
            .Where(t => t.Namespace == "CSharpFar.App.Bootstrap")
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetMethodBody()?.LocalVariables.Select(v => v.LocalType) ?? [])
            .Select(t => t.FullName)
            .ToArray();

        Assert.DoesNotContain(typeof(DpapiCredentialStore).FullName, bootstrapTypes);
        Assert.DoesNotContain(typeof(WindowsShellFileLauncher).FullName, bootstrapTypes);
        Assert.DoesNotContain(typeof(WindowsVolumeService).FullName, bootstrapTypes);
    }

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
    public void UnixPlatformServices_DoesNotUseWindowsOnlyImplementations()
    {
        var driver = new FakeConsoleDriver();
        using var platform = new UnixPlatformServices(
            driver,
            driver,
            new ShellService("/bin/sh", "-c \"{0}\""),
            new UnixShellFileLauncher(new UnixExecutableFileDetector()),
            new FileCredentialStore(Path.GetTempPath()),
            new UnixVolumeService(),
            new VolumeInfoService(),
            new FileSystemLocationService(),
            new UnixVolumeMountPointService(),
            new UnixFileSystemPlatformOperations());

        Assert.IsNotType<WindowsShellFileLauncher>(platform.FileLauncher);
        Assert.IsNotType<DpapiCredentialStore>(platform.CredentialStore);
        Assert.IsNotType<WindowsVolumeService>(platform.VolumeService);
        Assert.IsType<UnixShellFileLauncher>(platform.FileLauncher);
        Assert.IsType<FileCredentialStore>(platform.CredentialStore);
        Assert.IsType<UnixVolumeService>(platform.VolumeService);
        Assert.IsType<UnixFileSystemPlatformOperations>(platform.FileSystemOperations);
    }
}
