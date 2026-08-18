using CSharpFar.Console;
using CSharpFar.Console.Ansi;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Shell;

namespace CSharpFar.Platform.MacOs;

public sealed class MacOsPlatformServices : IPlatformServices
{
    private readonly IDisposable? _disposableConsoleDriver;
    internal MacOsPlatformServices(IConsoleDriver consoleDriver, ITerminalScreenMode terminalScreenMode, IShellService shellService,
        IFileLauncher fileLauncher, ICredentialStore credentialStore, IVolumeService volumeService, IVolumeInfoService volumeInfoService,
        IFileSystemLocationService locationService, IVolumeMountPointService volumeMountPointService, IFileSystemPlatformOperations fileSystemOperations)
    {
        _disposableConsoleDriver = consoleDriver as IDisposable; ConsoleDriver = consoleDriver; TerminalScreenMode = terminalScreenMode;
        ShellService = shellService; FileLauncher = fileLauncher; CredentialStore = credentialStore; VolumeService = volumeService;
        VolumeInfoService = volumeInfoService; LocationService = locationService; VolumeMountPointService = volumeMountPointService;
        FileSystemOperations = fileSystemOperations; ProcessesAndPorts = new UnsupportedProcessesAndPortsPlatformService();
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

    public static MacOsPlatformServices Create(string configDirectory, AppSettings.ShellSettings shellSettings)
    {
        var driver = AnsiTerminalConsoleDriver.CreateMacOs();
        return new MacOsPlatformServices(driver, driver,
            new ShellService(new UnixShellCommandLineBuilder(shellSettings.Executable), ShellComposition.CreateRegistry()),
            new UnixShellFileLauncher(new UnixExecutableFileDetector(), new MacOsAssociationLauncher()), new FileCredentialStore(configDirectory),
            new MacOsVolumeService(), new VolumeInfoService(), new FileSystemLocationService(), new MacOsVolumeMountPointService(),
            new MacOsFileSystemPlatformOperations());
    }
    public static AppSettings CreateDefaultSettings()
    {
        var settings = new AppSettings(); settings.Shell.Executable = "/bin/sh"; settings.Shell.ArgumentsFormat = "-c"; return settings;
    }
    public void Dispose() => _disposableConsoleDriver?.Dispose();
}
