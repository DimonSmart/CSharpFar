using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Services;
using CSharpFar.FarNetHost;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;
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
        FarNetModuleHost? farNetModuleHost = null,
        bool enableBuiltInNetworkModules = true,
        string? configDirectory = null,
        ITextClipboard? clipboard = null) =>
        new(ApplicationServicesBuilder.Create(
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
            farNetModuleHost,
            enableBuiltInNetworkModules,
            configDirectory,
            clipboard));

}
