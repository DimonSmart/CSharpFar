using CSharpFar.App.AutoRefresh;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.App.Files;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Menu;
using CSharpFar.App.Modules;
using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.App.UserMenu;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.History;
using CSharpFar.Core.Services;
using CSharpFar.FarNetHost;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal sealed class ApplicationServices
{
    public required ScreenRenderer Screen { get; init; }
    public required IFileSystemService FileSystem { get; init; }
    public required PanelController PanelController { get; init; }
    public required IShellService Shell { get; init; }
    public required IFileLauncher FileLauncher { get; init; }
    public required IFileOperationService FileOperations { get; init; }
    public required ISearchService SearchService { get; init; }
    public required FilePanelSourceRegistry SourceRegistry { get; init; }
    public required IHistoryStore History { get; init; }
    public required CommandHistoryNavigator CommandHistoryNavigator { get; init; }
    public required CommandCompletionController CommandCompletionController { get; init; }
    public required AppSettingsAlias Settings { get; init; }
    public required UserMenuStore UserMenu { get; init; }
    public required ITextClipboard Clipboard { get; init; }
    public required ApplicationSession Session { get; init; }
    public required DefaultMenuDefinitionProvider MenuProvider { get; init; }
    public required DefaultFunctionKeyBindingProvider FunctionKeyBindingProvider { get; init; }
    public required IReadOnlyList<FunctionKeyBinding> FunctionKeyBindings { get; init; }
    public required MenuLayoutService MenuLayoutService { get; init; }
    public required IFileHighlightService? HighlightService { get; init; }
    public required ApplicationServiceCallbacks Callbacks { get; init; }
    public required ChangeDirectoryCommandExecutor ChangeDirectoryCommandExecutor { get; init; }
    public required TopMenuController MenuController { get; init; }
    public required PanelAutoRefreshService AutoRefresh { get; init; }
    public required ApplicationPanelWorkspaceRenderer PanelWorkspaceRenderer { get; init; }
    public required ClockRenderer ClockRenderer { get; init; }
    public required PanelSortServiceFacade PanelSort { get; init; }
    public required PanelNavigationService PanelNavigation { get; init; }
    public required PanelSearchResultsService SearchResults { get; init; }
    public required PanelRefreshService PanelRefresh { get; init; }
    public required PanelQuickSearchController PanelQuickSearch { get; init; }
    public required PanelFileViewerService PanelFileViewer { get; init; }
    public required PanelFileOpener PanelFileOpener { get; init; }
    public required NativeModuleCatalog ModuleCatalog { get; init; }
    public required ModulePanelOpener ModulePanelOpener { get; init; }
    public required FarNetPanelActionService FarNetPanelActions { get; init; }
    public required ApplicationCommandRegistry CommandRegistry { get; init; }
    public required ApplicationFunctionKeyBarRenderer FunctionKeyBarRenderer { get; init; }
    public required ApplicationOverlayRenderer OverlayRenderer { get; init; }
    public required ApplicationCommandLineRenderer CommandLineRenderer { get; init; }
    public required TerminalSurfaceController TerminalSurface { get; init; }
    public required QuickViewDirectorySizeController QuickViewDirectorySize { get; init; }
    public required ApplicationRuntime Runtime { get; init; }
    public required KeyboardInputContext KeyboardInputContext { get; init; }
    public required KeyboardInputRouter KeyboardInputRouter { get; init; }
    public required MouseInputContext MouseInputContext { get; init; }
    public required MouseInputRouter MouseInputRouter { get; init; }
    public required string ConfigDirectory { get; init; }
    public required bool EnableBuiltInNetworkModules { get; init; }
    public required ICredentialStore? CredentialStore { get; init; }
    public required SftpModule? SftpModule { get; init; }
    public required FtpModule? FtpModule { get; init; }
    public required FarNetModuleHost? FarNetModuleHost { get; init; }
    public Action? SaveSettings { get; init; }
    public IVolumeService? VolumeService { get; init; }
    public IVolumeInfoService? VolumeInfoService { get; init; }
    public IFileSystemChangeWatcher? ChangeWatcher { get; init; }
    public IFileSystemLocationService? LocationService { get; init; }
    public IVolumeMountPointService? MountPointService { get; init; }
}
