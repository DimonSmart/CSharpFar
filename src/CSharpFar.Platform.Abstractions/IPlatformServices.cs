using CSharpFar.Console;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Platform.Abstractions;

public interface IPlatformServices : IDisposable
{
    IConsoleDriver ConsoleDriver { get; }
    IShellService ShellService { get; }
    IFileLauncher FileLauncher { get; }
    ICredentialStore CredentialStore { get; }
    IVolumeService VolumeService { get; }
    IVolumeInfoService VolumeInfoService { get; }
    IFileSystemLocationService LocationService { get; }
    IVolumeMountPointService VolumeMountPointService { get; }
    ITerminalScreenMode TerminalScreenMode { get; }
}
