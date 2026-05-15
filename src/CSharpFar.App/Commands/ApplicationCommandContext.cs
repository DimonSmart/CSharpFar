using CSharpFar.App.Rendering;
using CSharpFar.App.Plugins;
using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Commands;

internal sealed class ApplicationCommandContext
{
    private readonly Application _application;

    public ApplicationCommandContext(Application application)
    {
        _application = application;
    }

    public ScreenRenderer Screen => _application.CommandScreen;

    public ConsolePalette Palette => _application.CommandPalette;

    public ConsolePalette CommandPalette
    {
        get => _application.CommandPalette;
        set => _application.CommandPalette = value;
    }

    public PanelController Controller => _application.CommandPanelController;

    public IFileLauncher FileLauncher => _application.CommandFileLauncher;

    public IFileOperationService FileOperations => _application.CommandFileOperations;

    public ISearchService SearchService => _application.CommandSearchService;

    public IHistoryStore History => _application.CommandHistory;

    public CommandLineState CommandLine => _application.CommandLine;

    public UserMenuStore UserMenu => _application.CommandUserMenu;

    public AppSettingsAlias Settings => _application.CommandSettings;

    public IVolumeService? VolumeService => _application.CommandVolumeService;

    public IReadOnlyList<PluginMenuProjection> PluginDiskMenuItems =>
        _application.PluginDiskMenuItems;

    public FilePanelState LeftPanel => _application.CommandLeftPanel;

    public FilePanelState RightPanel => _application.CommandRightPanel;

    public FilePanelState ActiveState => _application.ActiveState;

    public FilePanelState PassiveState => _application.PassiveState;

    public PanelSide ActiveSide
    {
        get => _application.ActiveSide;
        set => _application.ActiveSide = value;
    }

    public bool Running
    {
        get => _application.Running;
        set => _application.Running = value;
    }

    public bool QuickView
    {
        get => _application.QuickView;
        set => _application.QuickView = value;
    }

    public PanelViewMode LeftViewMode
    {
        get => _application.LeftViewMode;
        set => _application.LeftViewMode = value;
    }

    public PanelViewMode RightViewMode
    {
        get => _application.RightViewMode;
        set => _application.RightViewMode = value;
    }

    public IFileHighlightService? HighlightService
    {
        get => _application.HighlightService;
        set => _application.HighlightService = value;
    }

    public AppSettingsAlias.PanelOptionsSettings PanelOptions =>
        _application.PanelOptions;

    public bool CanSaveSettings => _application.CanSaveSettings;

    public void SaveSettings() => _application.SaveSettings();

    public int VisibleRows() => _application.VisibleRows();

    public int VisibleRows(PanelSide side) => _application.VisibleRows(side);

    public FilePanelState GetPanelState(PanelSide side) =>
        _application.GetPanelState(side);

    public void RefreshPanels() => _application.RefreshPanels();

    public void RefreshPanelsAfterFileOperation() =>
        _application.RefreshPanelsAfterFileOperation();

    public void SafeRefresh(FilePanelState state, int visibleRows) =>
        _application.SafeRefresh(state, visibleRows);

    public void StartWatching(FilePanelState state, PanelSide side) =>
        _application.StartWatching(state, side);

    public void SortVirtualPanel(FilePanelState state, string? keepCursorPath) =>
        _application.SortVirtualPanel(state, keepCursorPath);

    public void SetPanelSortMode(
        FilePanelState state,
        SortMode sortMode,
        int visibleRows) =>
        _application.SetPanelSortMode(state, sortMode, visibleRows);

    public void OpenSearchResultsPanel(
        FilePanelState state,
        SearchRequest request,
        IReadOnlyList<SearchResultItem> results,
        bool cancelled) =>
        _application.OpenSearchResultsPanel(state, request, results, cancelled);

    public void GoToSearchResult(
        FilePanelState state,
        PanelSide side,
        SearchResultItem result) =>
        _application.GoToSearchResult(state, side, result);

    public void GoToSearchResult(
        FilePanelState state,
        PanelSide side,
        FilePanelItem result) =>
        _application.GoToSearchResult(state, side, result);

    public void OpenPanelItem(
        FilePanelState state,
        PanelSide side,
        FilePanelItem item) =>
        _application.OpenPanelItem(state, side, item);

    public ApplicationCommandResult OpenPluginMenuItem(Guid pluginId, Guid itemId) =>
        _application.OpenPluginMenuItem(pluginId, itemId);

    public ApplicationCommandResult OpenPluginDiskMenuItem(Guid pluginId, Guid itemId, PanelSide panelSide) =>
        _application.OpenPluginDiskMenuItem(pluginId, itemId, panelSide);

    public string CombinePanelPath(FilePanelState state, string name) =>
        _application.CombinePanelPath(state, name);

    public void ViewPanelFile(FilePanelState state, FilePanelItem item) =>
        _application.ViewPanelFile(state, item);

    public void ExecuteCommand(string command) =>
        _application.ExecuteCommand(command);

    public void ExecuteInCurrentConsole(
        string workDir,
        string displayCommand,
        Action execute) =>
        _application.ExecuteInCurrentConsole(workDir, displayCommand, execute);

    public void ResetFunctionKeyLayer() => _application.ResetFunctionKeyLayer();

    public void HideCommandCompletion(bool temporarily) =>
        _application.HideCommandCompletion(temporarily);

    public void ResetCommandHistoryNavigation() =>
        _application.ResetCommandHistoryNavigation();

    public bool TogglePanelVisibility(PanelSide side) =>
        _application.TogglePanelVisibility(side);

    public bool OpenTopMenu() => _application.OpenTopMenu();

    public void ShowReadOnlyPanelMessage(string action) =>
        _application.ShowReadOnlyPanelMessage(action);

    public bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        Application.HasCapability(state, capability);

    public FileOperationOptions BuildFileOperationOptions() =>
        _application.BuildFileOperationOptions();

    public FileOperationResult ExecuteFileOperation(FileOperationRequest request) =>
        new FileOperationUiRunner(
            Screen,
            () => Palette,
            FileOperations,
            () => Settings.FileOperations.ShowTotalProgress,
            _application.TryReadConsoleKeyForCommand).Execute(request);

    public IFileHighlightService? CreateHighlightService() =>
        Application.CreateHighlightService(Settings);
}
