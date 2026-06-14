using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Shell;

namespace CSharpFar.Platform.Windows;

public sealed class WindowsPlatformServices : IPlatformServices
{
    private readonly IDisposable? _disposableConsoleDriver;

    internal WindowsPlatformServices(
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

    public static WindowsPlatformServices Create(string configDirectory, AppSettings.ShellSettings shellSettings)
    {
        var consoleDriver = CreateConsoleDriver();
        return new WindowsPlatformServices(
            consoleDriver,
            consoleDriver,
            new ShellService(shellSettings.Executable, shellSettings.ArgumentsFormat),
            new WindowsShellFileLauncher(new WindowsExecutableFileDetector()),
            new DpapiCredentialStore(configDirectory),
            new WindowsVolumeService(),
            new VolumeInfoService(),
            new FileSystemLocationService(),
            new VolumeMountPointService());
    }

    public static AppSettings CreateDefaultSettings()
    {
        var settings = new AppSettings();
        settings.Shell.Executable = "cmd.exe";
        settings.Shell.ArgumentsFormat = "/c {0}";
        return settings;
    }

    public void Dispose() => _disposableConsoleDriver?.Dispose();

    private static SystemConsoleDriver CreateConsoleDriver() => new();
}
