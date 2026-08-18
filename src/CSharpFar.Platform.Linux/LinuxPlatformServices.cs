using CSharpFar.Console;
using CSharpFar.Console.Ansi;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Shell;

namespace CSharpFar.Platform.Linux;

public sealed class LinuxPlatformServices : IPlatformServices
{
    private readonly IDisposable? _disposableConsoleDriver;

    internal LinuxPlatformServices(
        IConsoleDriver consoleDriver,
        ITerminalScreenMode terminalScreenMode,
        IShellService shellService,
        IFileLauncher fileLauncher,
        ICredentialStore credentialStore,
        IVolumeService volumeService,
        IVolumeInfoService volumeInfoService,
        IFileSystemLocationService locationService,
        IVolumeMountPointService volumeMountPointService,
        IFileSystemPlatformOperations fileSystemOperations,
        IProcessesAndPortsPlatformService? processesAndPorts = null)
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
        FileSystemOperations = fileSystemOperations;
        TerminalScreenMode = terminalScreenMode;
        ProcessesAndPorts = processesAndPorts ?? new UnsupportedProcessesAndPortsPlatformService();
    }

    public IConsoleDriver ConsoleDriver { get; }
    public IShellService ShellService { get; }
    public IFileLauncher FileLauncher { get; }
    public ICredentialStore CredentialStore { get; }
    public IVolumeService VolumeService { get; }
    public IVolumeInfoService VolumeInfoService { get; }
    public IFileSystemLocationService LocationService { get; }
    public IVolumeMountPointService VolumeMountPointService { get; }
    public IFileSystemPlatformOperations FileSystemOperations { get; }
    public ITerminalScreenMode TerminalScreenMode { get; }
    public IProcessesAndPortsPlatformService ProcessesAndPorts { get; }

    public static LinuxPlatformServices Create(string configDirectory, AppSettings.ShellSettings shellSettings)
    {
        var consoleDriver = CreateConsoleDriver();
        return new LinuxPlatformServices(
            consoleDriver,
            consoleDriver,
            new ShellService(new UnixShellCommandLineBuilder(shellSettings.Executable), ShellComposition.CreateRegistry()),
            new UnixShellFileLauncher(new UnixExecutableFileDetector(), new UnixAssociationLauncher(new UnixEnvironment())),
            new FileCredentialStore(configDirectory),
            new LinuxVolumeService(),
            new VolumeInfoService(),
            new FileSystemLocationService(),
            new LinuxVolumeMountPointService(),
            new LinuxFileSystemPlatformOperations(),
            new UnsupportedProcessesAndPortsPlatformService());
    }

    public static AppSettings CreateDefaultSettings()
    {
        var settings = new AppSettings();
        settings.Shell.Executable = "/bin/sh";
        settings.Shell.ArgumentsFormat = "-c";
        return settings;
    }

    public void Dispose() => _disposableConsoleDriver?.Dispose();

    private static AnsiTerminalConsoleDriver CreateConsoleDriver() => AnsiTerminalConsoleDriver.CreateLinux();
}
