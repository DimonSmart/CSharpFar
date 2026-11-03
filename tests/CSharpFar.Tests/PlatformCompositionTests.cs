using System.Security.Cryptography;
using CSharpFar.FileSystem;
using CSharpFar.Platform.Abstractions;
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
        Assert.IsType<WindowsProcessesAndPortsPlatformService>(platform.ProcessesAndPorts);
        Assert.IsType<WindowsFileUsagePlatformService>(platform.FileUsage);
        Assert.NotNull(platform.NetworkTrafficMonitorFactory);
    }

    [Fact]
    public void DpapiStore_ReadsLegacyCurrentUserCredentialsWithoutChangingTheFormat()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = Path.Combine(Path.GetTempPath(), "CSharpFar.Dpapi." + Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, "credentials");
        Directory.CreateDirectory(directory);
        try
        {
            byte[] entropy = "CSharpFar.Credential.v1"u8.ToArray();
            byte[] encrypted = ProtectedData.Protect("existing credential"u8.ToArray(),
                entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(directory, "old.bin"), encrypted);

            var store = new DpapiCredentialStore(root);
            Assert.Equal("existing credential", store.TryReadPassword("old"));

            store.SavePassword("new", "new password");
            byte[] saved = File.ReadAllBytes(Path.Combine(directory, "new.bin"));
            Assert.Equal("new password", System.Text.Encoding.UTF8.GetString(
                ProtectedData.Unprotect(saved, entropy, DataProtectionScope.CurrentUser)));
            store.DeletePassword("new");
            Assert.Null(store.TryReadPassword("new"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
        Assert.False(platform.ProcessesAndPorts.Support.IsSupported);
        Assert.DoesNotContain("Windows only", platform.ProcessesAndPorts.Support.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<UnsupportedFileUsagePlatformService>(platform.FileUsage);
        Assert.Null(platform.NetworkTrafficMonitorFactory);
    }

    [Fact]
    public void MacOsPlatformServices_UsesMacOsProcessesAndPorts()
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
        MacOsProcessesAndPortsPlatformService processes = Assert.IsType<MacOsProcessesAndPortsPlatformService>(platform.ProcessesAndPorts);
        Assert.True(processes.Support.IsSupported);
        Assert.True(processes.Support.CanTerminate);
        Assert.IsType<UnsupportedFileUsagePlatformService>(platform.FileUsage);
        Assert.Null(platform.NetworkTrafficMonitorFactory);
    }
}
