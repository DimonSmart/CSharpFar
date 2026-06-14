using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Shell;

namespace CSharpFar.Platform.Unix;

public sealed class UnixPlatformServices : IPlatformServices
{
    private readonly IDisposable? _disposableConsoleDriver;

    internal UnixPlatformServices(
        IConsoleDriver consoleDriver,
        ITerminalScreenMode terminalScreenMode,
        IShellService shellService,
        IFileLauncher fileLauncher,
        ICredentialStore credentialStore,
        IVolumeService volumeService,
        IVolumeInfoService volumeInfoService,
        IFileSystemLocationService locationService,
        IVolumeMountPointService volumeMountPointService)
    {
        _disposableConsoleDriver = consoleDriver as IDisposable;
        ConsoleDriver = consoleDriver;
        ShellService = shellService;
        FileLauncher = fileLauncher;
        CredentialStore = credentialStore;
        VolumeService = volumeService;
        VolumeInfoService = volumeInfoService;
        LocationService = locationService;
        VolumeMountPointService = volumeMountPointService;
        TerminalScreenMode = terminalScreenMode;
    }

    public IConsoleDriver ConsoleDriver { get; }
    public IShellService ShellService { get; }
    public IFileLauncher FileLauncher { get; }
    public ICredentialStore CredentialStore { get; }
    public IVolumeService VolumeService { get; }
    public IVolumeInfoService VolumeInfoService { get; }
    public IFileSystemLocationService LocationService { get; }
    public IVolumeMountPointService VolumeMountPointService { get; }
    public ITerminalScreenMode TerminalScreenMode { get; }

    public static UnixPlatformServices Create(string configDirectory, AppSettings.ShellSettings shellSettings)
    {
        var consoleDriver = CreateConsoleDriver();
        return new UnixPlatformServices(
            consoleDriver,
            consoleDriver,
            new ShellService(shellSettings.Executable, shellSettings.ArgumentsFormat),
            new UnixShellFileLauncher(new UnixExecutableFileDetector()),
            new FileCredentialStore(configDirectory),
            new UnixVolumeService(),
            new VolumeInfoService(),
            new FileSystemLocationService(),
            new UnixVolumeMountPointService());
    }

    public static AppSettings CreateDefaultSettings()
    {
        var settings = new AppSettings();
        settings.Shell.Executable = "/bin/sh";
        settings.Shell.ArgumentsFormat = "-c \"{0}\"";
        return settings;
    }

    public void Dispose() => _disposableConsoleDriver?.Dispose();

    private static SystemConsoleDriver CreateConsoleDriver() => new();
}
