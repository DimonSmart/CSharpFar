using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Files;
using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.History;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.App.Menu;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal static class CoreServicesFactory
{
    public static CoreServices Create(
        IFileSystemService fs,
        IHistoryStore? history,
        AppSettingsAlias? settings,
        UserMenuStore? userMenu,
        IVolumeInfoService? volumeInfoService,
        IVolumeMountPointService? mountPointService,
        IFileLauncher? fileLauncher,
        ISearchService? searchService,
        FilePanelSourceRegistry? sourceRegistry,
        string? configDirectory,
        ITextClipboard? clipboard)
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
        var session = ApplicationSessionFactory.Create(effectiveSettings, controller);
        var effectiveConfigDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CSharpFar");

        return new CoreServices(
            effectiveSettings,
            effectiveSourceRegistry,
            controller,
            effectiveHistory,
            session,
            effectiveConfigDirectory,
            searchService ?? new FileSystemSearchService(),
            fileLauncher ?? MissingPlatformFileLauncher.Instance,
            clipboard ?? TextCopyTextClipboard.Instance,
            userMenu ?? new UserMenuStore(effectiveConfigDirectory),
            new DefaultFunctionKeyBindingProvider(),
            new DefaultMenuDefinitionProvider());
    }
}

internal sealed record CoreServices(
    AppSettingsAlias Settings,
    FilePanelSourceRegistry SourceRegistry,
    PanelController PanelController,
    IHistoryStore History,
    ApplicationSession Session,
    string ConfigDirectory,
    ISearchService SearchService,
    IFileLauncher FileLauncher,
    ITextClipboard Clipboard,
    UserMenuStore UserMenu,
    DefaultFunctionKeyBindingProvider FunctionKeyBindingProvider,
    DefaultMenuDefinitionProvider MenuProvider);

internal sealed class MissingPlatformFileLauncher : IFileLauncher
{
    public static readonly MissingPlatformFileLauncher Instance = new();

    private MissingPlatformFileLauncher()
    {
    }

    public FileLaunchMode GetLaunchMode(string fullPath) => FileLaunchMode.ShellAssociation;

    public void OpenFile(string fullPath, string workingDirectory) =>
        throw new InvalidOperationException("No platform file launcher is configured.");
}
