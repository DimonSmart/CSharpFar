using CSharpFar.App.CommandLine;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Menu;
using CSharpFar.App.Modules;
using CSharpFar.App.State;
using CSharpFar.App.UserMenu;
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
