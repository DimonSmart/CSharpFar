using CSharpFar.App.AutoRefresh;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Files;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Highlighting;
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
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Commands;

internal sealed class ApplicationCommandContext
{
    private readonly ApplicationSession _session;
    private readonly PanelWorkspaceController _panelWorkspace;
    private readonly PanelAutoRefreshService _autoRefresh;
    private readonly PanelRefreshService _panelRefresh;
    private readonly PanelSortServiceFacade _panelSort;
    private readonly PanelNavigationService _panelNavigation;
    private readonly PanelSearchResultsService _searchResults;
    private readonly PanelQuickSearchController _panelQuickSearch;
    private readonly PanelVisibilityController _panelVisibility;
    private readonly PanelFileViewerService _panelFileViewer;
    private readonly PanelFileOpener _panelFileOpener;
    private readonly NativeModuleCatalog _moduleCatalog;
    private readonly ModulePanelOpener _modulePanelOpener;
    private readonly CommandLineCommandExecutor _commandLineCommandExecutor;
    private readonly ExternalConsoleCommandRunner _externalConsoleCommandRunner;
    private readonly CommandCompletionController _commandCompletionController;
    private readonly CommandHistoryNavigator _commandHistoryNavigator;
    private readonly TopMenuController _menuController;
    private readonly DefaultMenuDefinitionProvider _menuProvider;
    private readonly Action? _saveSettings;
    private readonly IVolumeService? _volumeService;
    private readonly IFileMetadataService _fileMetadata;
    private readonly Func<IFileAttributesDialog> _fileAttributesDialogFactory;
    private IFileHighlightService? _highlightService;

    public ApplicationCommandContext(
        ScreenRenderer screen,
        PanelController controller,
        IFileLauncher fileLauncher,
        IFileOperationService fileOperations,
        ISearchService searchService,
        IHistoryStore history,
        UserMenuStore userMenu,
        ITextClipboard textClipboard,
        AppSettingsAlias settings,
        ApplicationSession session,
        DefaultMenuDefinitionProvider menuProvider,
        PanelWorkspaceController panelWorkspace,
        PanelAutoRefreshService autoRefresh,
        PanelRefreshService panelRefresh,
        PanelSortServiceFacade panelSort,
        PanelNavigationService panelNavigation,
        PanelSearchResultsService searchResults,
        PanelQuickSearchController panelQuickSearch,
        PanelVisibilityController panelVisibility,
        PanelFileViewerService panelFileViewer,
        PanelFileOpener panelFileOpener,
        NativeModuleCatalog moduleCatalog,
        ModulePanelOpener modulePanelOpener,
        CommandLineCommandExecutor commandLineCommandExecutor,
        ExternalConsoleCommandRunner externalConsoleCommandRunner,
        CommandCompletionController commandCompletionController,
        CommandHistoryNavigator commandHistoryNavigator,
        TopMenuController menuController,
        Action? saveSettings,
        IVolumeService? volumeService,
        IFileMetadataService fileMetadata,
        Func<IFileAttributesDialog> fileAttributesDialogFactory,
        IFileHighlightService? highlightService)
    {
        Screen = screen;
        Controller = controller;
        FileLauncher = fileLauncher;
        FileOperations = fileOperations;
        SearchService = searchService;
        History = history;
        UserMenu = userMenu;
        TextClipboard = textClipboard;
        Settings = settings;
        _session = session;
        _menuProvider = menuProvider;
        _panelWorkspace = panelWorkspace;
        _autoRefresh = autoRefresh;
        _panelRefresh = panelRefresh;
        _panelSort = panelSort;
        _panelNavigation = panelNavigation;
        _searchResults = searchResults;
        _panelQuickSearch = panelQuickSearch;
        _panelVisibility = panelVisibility;
        _panelFileViewer = panelFileViewer;
        _panelFileOpener = panelFileOpener;
        _moduleCatalog = moduleCatalog;
        _modulePanelOpener = modulePanelOpener;
        _commandLineCommandExecutor = commandLineCommandExecutor;
        _externalConsoleCommandRunner = externalConsoleCommandRunner;
        _commandCompletionController = commandCompletionController;
        _commandHistoryNavigator = commandHistoryNavigator;
        _menuController = menuController;
        _saveSettings = saveSettings;
        _volumeService = volumeService;
        _fileMetadata = fileMetadata;
        _fileAttributesDialogFactory = fileAttributesDialogFactory;
        _highlightService = highlightService;
    }

    public ScreenRenderer Screen { get; }

    public ConsolePalette Palette => _session.App.Palette;

    public ConsolePalette CommandPalette
    {
        get => _session.App.Palette;
        set => _session.App.Palette = value;
    }

    public PanelController Controller { get; }

    public IFileLauncher FileLauncher { get; }

    public IFileOperationService FileOperations { get; }

    public ISearchService SearchService { get; }

    public IHistoryStore History { get; }

    public CommandLineState CommandLine => _session.CommandLine.State;

    public UserMenuStore UserMenu { get; }

    public ITextClipboard TextClipboard { get; }

    public AppSettingsAlias Settings { get; }

    public IVolumeService? VolumeService => _volumeService;

    public IFileMetadataService FileMetadata => _fileMetadata;

    public IReadOnlyList<ModuleMenuProjection> ModuleDiskMenuItems =>
        _moduleCatalog.DiskMenuItems;

    public FilePanelState LeftPanel => _session.Panels.Left;

    public FilePanelState RightPanel => _session.Panels.Right;

    public FilePanelState ActiveState => _panelWorkspace.ActiveState;

    public FilePanelState PassiveState => ActiveSide == PanelSide.Left ? RightPanel : LeftPanel;

    public PanelSide ActiveSide
    {
        get => _panelWorkspace.ActiveSide;
        set => _panelWorkspace.SetActiveSide(value);
    }

    public bool Running
    {
        get => _session.App.Running;
        set => _session.App.Running = value;
    }

    public bool QuickView
    {
        get => _session.App.QuickView;
        set => _session.App.QuickView = value;
    }

    public PanelViewMode LeftViewMode
    {
        get => _session.Panels.LeftViewMode;
        set => _session.Panels.LeftViewMode = value;
    }

    public PanelViewMode RightViewMode
    {
        get => _session.Panels.RightViewMode;
        set => _session.Panels.RightViewMode = value;
    }

    public IFileHighlightService? HighlightService
    {
        get => _highlightService;
        set => _highlightService = value;
    }

    public AppSettingsAlias.PanelOptionsSettings PanelOptions => Settings.Panels.Options;

    public bool CanSaveSettings => _saveSettings is not null;

    public bool HasVisiblePanels => _panelWorkspace.HasVisiblePanels;

    public void SaveSettings() => _saveSettings?.Invoke();

    public IFileAttributesDialog CreateFileAttributesDialog() => _fileAttributesDialogFactory();

    public int VisibleRows() => _panelWorkspace.VisibleRows();

    public int VisibleRows(PanelSide side) => _panelWorkspace.VisibleRows(side);

    public FilePanelState GetPanelState(PanelSide side) =>
        _panelWorkspace.GetPanelState(side);

    public void RefreshPanels() => _panelRefresh.RefreshPanels(LeftPanel, RightPanel);

    public void RefreshPanelsAfterFileOperation() =>
        _panelRefresh.RefreshPanelsAfterFileOperation(LeftPanel, RightPanel);

    public void SafeRefresh(FilePanelState state, int visibleRows) =>
        _panelRefresh.SafeRefresh(state, visibleRows);

    public void StartWatching(FilePanelState state, PanelSide side) =>
        _autoRefresh.StartWatching(state, side);

    public void SortVirtualPanel(FilePanelState state, string? keepCursorPath, int visibleRows) =>
        _panelSort.SortVirtualPanel(state, keepCursorPath, visibleRows);

    public void SetPanelSortMode(
        FilePanelState state,
        SortMode sortMode,
        int visibleRows) =>
        _panelSort.SetPanelSortMode(state, sortMode, visibleRows);

    public void OpenSearchResultsPanel(
        FilePanelState state,
        SearchRequest request,
        IReadOnlyList<SearchResultItem> results,
        bool cancelled) =>
        _searchResults.OpenPanel(state, request, results, cancelled);

    public void GoToSearchResult(
        FilePanelState state,
        PanelSide side,
        SearchResultItem result) =>
        _searchResults.GoToResult(state, side, result);

    public void GoToSearchResult(
        FilePanelState state,
        PanelSide side,
        FilePanelItem result) =>
        _searchResults.GoToResult(state, side, result);

    public void OpenPanelItem(FilePanelState state, PanelSide side, FilePanelItem item)
    {
        if (state.SearchRequest is not null)
        {
            GoToSearchResult(state, side, item);
            return;
        }

        if (item.IsDirectory)
        {
            _panelNavigation.OpenDirectoryItem(state, side, item);
            return;
        }

        _panelFileOpener.OpenFileItem(ActiveState, item);
    }

    public ApplicationCommandResult OpenModuleMenuItem(Guid actionId) =>
        _modulePanelOpener.OpenMenuItem(actionId, ActiveSide);

    public ApplicationCommandResult OpenModuleDiskMenuItem(Guid actionId, PanelSide panelSide) =>
        _modulePanelOpener.OpenDiskMenuItem(actionId, panelSide);

    public string CombinePanelPath(FilePanelState state, string name)
    {
        if (state.SourceId == PanelSourceId.Local)
            return Path.Combine(state.SourcePath, name);

        string directory = state.SourcePath.TrimEnd('/');
        return directory.Length == 0 || directory == "/"
            ? "/" + name
            : directory + "/" + name;
    }

    public void ViewPanelFile(FilePanelState state, FilePanelItem item) =>
        _panelFileViewer.ViewPanelFile(state, item);

    public void ExecuteCommand(string command) =>
        _commandLineCommandExecutor.Execute(command);

    public void ExecuteInCurrentConsole(
        string workDir,
        string displayCommand,
        Action execute) =>
        _externalConsoleCommandRunner.Execute(workDir, displayCommand, execute);

    public void ResetFunctionKeyLayer() =>
        _session.FunctionKeyLayer = FunctionKeyLayer.Plain;

    public void HideCommandCompletion(bool temporarily) =>
        _commandCompletionController.Hide(temporarily);

    public void ResetCommandHistoryNavigation() =>
        _commandHistoryNavigator.Reset();

    public void ResetTransientNavigationUi()
    {
        _panelQuickSearch.Close();
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();
    }

    public bool TogglePanelVisibility(PanelSide side) =>
        _panelVisibility.TogglePanel(side);

    public bool OpenTopMenu()
    {
        _panelQuickSearch.Close();
        _menuController.HandleKey(
            new ConsoleKeyInfo('\0', ConsoleKey.F9, shift: false, alt: false, control: false),
            BuildMenuDefinition(),
            ActiveSide);
        return true;
    }

    public void ShowReadOnlyPanelMessage(string action)
    {
        new MessageDialog(Screen).Show(
            action,
            "The current panel source does not support this operation.");
    }

    public bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;

    public FileOperationOptions BuildFileOperationOptions() =>
        FileOperationOptionsFactory.Create(Settings);

    public FileOperationResult ExecuteFileOperation(FileOperationRequest request) =>
        new FileOperationUiRunner(
            Screen,
            () => Palette,
            FileOperations,
            () => Settings.FileOperations.ShowTotalProgress,
            TryReadConsoleKeyForCommand).Execute(request);

    public IFileHighlightService? CreateHighlightService() =>
        FileHighlightServiceFactory.Create(Settings);

    private MenuBarDefinition BuildMenuDefinition() =>
        _menuProvider.BuildMenu(new MenuBuildContext
        {
            ActivePanelSide = ActiveSide,
            LeftPanel = LeftPanel,
            RightPanel = RightPanel,
            LeftViewMode = LeftViewMode,
            RightViewMode = RightViewMode,
            Settings = Settings,
            CanSaveSettings = CanSaveSettings,
            ModuleMenuItems = _moduleCatalog.MenuItems,
        });

    private static ConsoleKeyInfo? TryReadConsoleKeyForCommand() =>
        TryReadConsoleKey(out var key) ? key : null;

    private static bool TryReadConsoleKey(out ConsoleKeyInfo key)
    {
        try
        {
            if (global::System.Console.KeyAvailable)
            {
                key = global::System.Console.ReadKey(intercept: true);
                return true;
            }
        }
        catch (InvalidOperationException)
        {
        }

        key = default;
        return false;
    }
}
