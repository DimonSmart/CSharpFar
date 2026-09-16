using CSharpFar.App.Dialogs;
using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;
using CSharpFar.Platform.Abstractions;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

public static class ApplicationFactory
{
    public static Application Create(
        ScreenRenderer screen,
        IFileSystemService fs,
        IShellService shell,
        IFileOperationService fileOps,
        IHistoryStore? history = null,
        AppSettingsAlias? settings = null,
        UserMenuStore? userMenu = null,
        Action? saveSettings = null,
        IVolumeService? volumeService = null,
        IVolumeInfoService? volumeInfoService = null,
        IFileSystemChangeWatcher? changeWatcher = null,
        IFileSystemLocationService? locationService = null,
        IVolumeMountPointService? mountPointService = null,
        IFileLauncher? fileLauncher = null,
        ISearchService? searchService = null,
        FilePanelSourceRegistry? sourceRegistry = null,
        ICredentialStore? credentialStore = null,
        SftpModule? sftpModule = null,
        FtpModule? ftpModule = null,
        bool enableBuiltInNetworkModules = true,
        string? configDirectory = null,
        ITextClipboard? clipboard = null,
        ITerminalScreenMode? terminalScreenMode = null,
        IFileMetadataService? fileMetadata = null,
        Func<IFileAttributesDialog>? fileAttributesDialogFactory = null,
        ApplicationRunOptions? runOptions = null,
        IProcessesAndPortsPlatformService? processesAndPorts = null,
        IFileUsagePlatformService? fileUsage = null,
        PlatformKind? platform = null)
    {
        var services = ApplicationServicesBuilder.Create(
            screen,
            fs,
            shell,
            fileOps,
            history,
            settings,
            userMenu,
            saveSettings,
            volumeService,
            volumeInfoService,
            changeWatcher,
            locationService,
            mountPointService,
            fileLauncher,
            searchService,
            sourceRegistry,
            credentialStore,
            sftpModule,
            ftpModule,
            enableBuiltInNetworkModules,
            configDirectory,
            clipboard,
            terminalScreenMode,
            fileMetadata,
            fileAttributesDialogFactory,
            runOptions,
            processesAndPorts,
            fileUsage);

        services.CommandContext.UserMenu.RuntimePlatform = platform ?? DetectCurrentPlatform();
        return new Application(services);
    }

    private static PlatformKind DetectCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return PlatformKind.Windows;
        if (OperatingSystem.IsMacOS())
            return PlatformKind.MacOs;
        if (OperatingSystem.IsLinux())
            return PlatformKind.Linux;

        throw new PlatformNotSupportedException("CSharpFar does not support the current operating system.");
    }
}
