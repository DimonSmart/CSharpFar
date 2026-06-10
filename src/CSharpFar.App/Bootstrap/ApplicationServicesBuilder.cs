using CSharpFar.App.CommandLine;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Highlighting;
using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.History;
using CSharpFar.Core.Services;
using CSharpFar.FarNetHost;
using CSharpFar.FileSystem;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;
using CSharpFar.Shell;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal static class ApplicationServicesBuilder
{
    public static ApplicationServices Create(
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
        ITextClipboard? clipboard = null)
    {
        var effectiveSettings = settings ?? new AppSettingsAlias();
        var effectiveSourceRegistry = sourceRegistry ?? new FilePanelSourceRegistry([new LocalFilePanelSource(fs)]);
        var sortService = new PanelSortService();
        var viewBuilder = new PanelViewBuilder(
            fs,
            sortService,
            volumeInfoService,
            mountPoints: mountPointService,
            sources: effectiveSourceRegistry);
        var controller = new PanelController(viewBuilder);
        var effectiveHistory = history ?? new InMemoryHistoryStore();
        var functionKeyBindingProvider = new DefaultFunctionKeyBindingProvider();
        var session = ApplicationSessionFactory.Create(effectiveSettings, controller);
        var effectiveConfigDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CSharpFar");

        return new ApplicationServices
        {
            Screen = screen,
            FileSystem = fs,
            PanelController = controller,
            Shell = shell,
            FileLauncher = fileLauncher ?? new WindowsShellFileLauncher(),
            FileOperations = fileOps,
            SearchService = searchService ?? new FileSystemSearchService(),
            SourceRegistry = effectiveSourceRegistry,
            History = effectiveHistory,
            CommandHistoryNavigator = new CommandHistoryNavigator(effectiveHistory),
            CommandCompletionController = new CommandCompletionController(effectiveHistory, session.CommandLine.Completion),
            Settings = effectiveSettings,
            UserMenu = userMenu ?? new UserMenuStore(effectiveConfigDirectory),
            Clipboard = clipboard ?? TextCopyTextClipboard.Instance,
            Session = session,
            MenuProvider = new(),
            FunctionKeyBindingProvider = functionKeyBindingProvider,
            FunctionKeyBindings = functionKeyBindingProvider.GetBindings(),
            MenuLayoutService = new(),
            HighlightService = FileHighlightServiceFactory.Create(effectiveSettings),
            ConfigDirectory = effectiveConfigDirectory,
            EnableBuiltInNetworkModules = enableBuiltInNetworkModules,
            CredentialStore = credentialStore,
            SftpModule = sftpModule,
            FtpModule = ftpModule,
            FarNetModuleHost = farNetModuleHost,
            SaveSettings = saveSettings,
            VolumeService = volumeService,
            VolumeInfoService = volumeInfoService,
            ChangeWatcher = changeWatcher,
            LocationService = locationService,
            MountPointService = mountPointService,
        };
    }
}
