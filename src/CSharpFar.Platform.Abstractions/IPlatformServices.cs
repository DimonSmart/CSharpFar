using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Platform.Abstractions;

public interface IPlatformServices : IDisposable
{
    PlatformKind Platform { get; }
    IConsoleDriver ConsoleDriver { get; }
    IShellService ShellService { get; }
    IFileLauncher FileLauncher { get; }
    ICredentialStore CredentialStore { get; }
    IVolumeService VolumeService { get; }
    IVolumeInfoService VolumeInfoService { get; }
    IFileSystemLocationService LocationService { get; }
    IVolumeMountPointService VolumeMountPointService { get; }
    IFileSystemPlatformOperations FileSystemOperations { get; }
    ITerminalScreenMode TerminalScreenMode { get; }
    IProcessesAndPortsPlatformService ProcessesAndPorts { get; }
    IFileUsagePlatformService FileUsage { get; }
}
