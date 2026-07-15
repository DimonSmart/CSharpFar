using CSharpFar.App.Dialogs;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Commands;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Files;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Highlighting;
using CSharpFar.App.Input;
using CSharpFar.App.Menu;
using CSharpFar.App.Modules;
using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.App.Editor;
using CSharpFar.App.UserMenu;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Module.Abstractions;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App;

public sealed class Application
{
    private const int MaxCommandCompletionRows = CommandHistoryCompletionRenderer.MaxVisibleRows;

    private readonly ScreenRenderer _screen;
    private readonly ApplicationRuntime _runtime;
    private readonly PanelController _ctrl;
    private readonly PanelFileViewerService _panelFileViewer;
    private readonly PanelFileOpener _panelFileOpener;
    private readonly PanelAutoRefreshService _autoRefresh;
    private readonly ApplicationRenderContext _renderContext;
    private readonly ApplicationRenderCoordinator _renderCoordinator;
    private readonly ApplicationUiSurface _applicationSurface;
    private readonly UiCompositionHost _composition;
    private readonly KeyboardInputContext _keyboardInputContext;
    private readonly KeyboardInputRouter _keyboardInputRouter;
    private readonly MouseInputRouter _mouseInputRouter;
    private readonly ApplicationInputDispatcher _applicationInputDispatcher;
    private readonly TerminalSurfaceController _terminalSurface;
    private readonly PanelRefreshService _panelRefresh;
    private readonly PanelSearchResultsService _searchResults;
    private readonly PanelSortServiceFacade _panelSort;
    private readonly PanelNavigationService _panelNavigation;
    private readonly PanelWorkspaceController _panelWorkspace;
    private readonly PanelVisibilityController _panelVisibility;
    private readonly NativeModuleCatalog _moduleCatalog;
    private readonly ModulePanelOpener _modulePanelOpener;
    private readonly CommandHistoryNavigator _commandHistoryNavigator;
    private readonly CommandCompletionController _commandCompletionController;
    private readonly CommandLineCommandExecutor _commandLineCommandExecutor;
    private readonly ExternalConsoleCommandRunner _externalConsoleCommandRunner;
    private readonly AppSettingsAlias _settings;
    private readonly Action? _saveSettings;
    private readonly ITextClipboard _clipboard;

    private readonly ApplicationSession _session;
    private readonly PanelQuickSearchController _panelQuickSearch;

    private FilePanelState _left => _session.Panels.Left;
    private FilePanelState _right => _session.Panels.Right;
    private CommandLineState _cmdLine => _session.CommandLine.State;
    private CommandCompletionState _commandCompletion => _session.CommandLine.Completion;
    private PanelSide _active
    {
        get => _session.Panels.ActiveSide;
        set => _session.Panels.ActiveSide = value;
    }
    private ApplicationState _state => _session.App;
    private UiTransientState _ui => _session.Ui;
    private PanelViewMode _leftViewMode
    {
        get => _session.Panels.LeftViewMode;
        set => _session.Panels.LeftViewMode = value;
    }
    private PanelViewMode _rightViewMode
    {
        get => _session.Panels.RightViewMode;
        set => _session.Panels.RightViewMode = value;
    }
    private MenuState _menuState => _session.Menu.State;
    private readonly DefaultMenuDefinitionProvider _menuProvider;
    private readonly ApplicationCommandRegistry _commandRegistry;
    private readonly ApplicationCommandContext _commandContext;
    private readonly TopMenuController      _menuController;
    private FunctionKeyLayer _functionKeyLayer
    {
        get => _session.FunctionKeyLayer;
        set => _session.FunctionKeyLayer = value;
    }

    public Application(
        ScreenRenderer         screen,
        IFileSystemService     fs,
        IShellService          shell,
        IFileOperationService  fileOps,
        IHistoryStore?         history          = null,
        AppSettingsAlias?      settings         = null,
        UserMenuStore?         userMenu         = null,
        Action?                saveSettings     = null,
        IVolumeService?              volumeService     = null,
        IVolumeInfoService?          volumeInfoService  = null,
        IFileSystemChangeWatcher?    changeWatcher     = null,
        IFileSystemLocationService?  locationService   = null,
        IVolumeMountPointService?    mountPointService = null,
        IFileLauncher?               fileLauncher      = null,
        ISearchService?              searchService     = null,
        FilePanelSourceRegistry?     sourceRegistry    = null,
        ICredentialStore?            credentialStore   = null,
        SftpModule?                  sftpModule        = null,
        FtpModule?                   ftpModule         = null,
        bool                         enableBuiltInNetworkModules = true,
        string?                      configDirectory   = null,
        ITextClipboard?              clipboard         = null,
        ITerminalScreenMode?         terminalScreenMode = null,
        IFileMetadataService?        fileMetadata = null,
        Func<IFileAttributesDialog>? fileAttributesDialogFactory = null)
        : this(ApplicationServicesBuilder.Create(
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
            fileAttributesDialogFactory))
    {
    }

    internal Application(ApplicationServices services)
    {
        _screen = services.Screen;
        _ctrl = services.PanelController;
        _commandHistoryNavigator = services.CommandHistoryNavigator;
        _commandCompletionController = services.CommandCompletionController;
        _commandLineCommandExecutor = services.CommandLineCommandExecutor;
        _externalConsoleCommandRunner = services.ExternalConsoleCommandRunner;
        _settings = services.Settings;
        _clipboard = services.Clipboard;
        _saveSettings = services.SaveSettings;
        _session = services.Session;
        _menuProvider = services.MenuProvider;
        _menuController = services.MenuController;
        _autoRefresh = services.AutoRefresh;
        _renderContext = services.RenderContext;
        _renderCoordinator = services.RenderCoordinator;
        _applicationSurface = services.ApplicationSurface;
        _composition = services.Composition;
        _panelSort = services.PanelSort;
        _panelNavigation = services.PanelNavigation;
        _searchResults = services.SearchResults;
        _panelRefresh = services.PanelRefresh;
        _panelQuickSearch = services.PanelQuickSearch;
        _panelWorkspace = services.PanelWorkspace;
        _panelVisibility = services.PanelVisibility;
        _panelFileViewer = services.PanelFileViewer;
        _panelFileOpener = services.PanelFileOpener;
        _moduleCatalog = services.ModuleCatalog;
        _modulePanelOpener = services.ModulePanelOpener;
        _commandRegistry = services.CommandRegistry;
        _commandContext = services.CommandContext;
        _keyboardInputContext = services.KeyboardInputContext;
        _keyboardInputRouter = services.KeyboardInputRouter;
        _mouseInputRouter = services.MouseInputRouter;
        _applicationInputDispatcher = services.ApplicationInputDispatcher;
        _terminalSurface = services.TerminalSurface;
        BindCallbacks(services.Callbacks);
        BindKeyboardInputContext(_keyboardInputContext);
        BindMouseInputContext(services.MouseInputContext);
        BindRenderContext(_renderContext);
        _runtime = services.Runtime;

    }

    private void BindKeyboardInputContext(KeyboardInputContext context)
    {
        context.ExecuteRegisteredCommand = ExecuteRegisteredCommand;
        context.SelectAllCommandLineTextOrPanelItems = SelectAllCommandLineTextOrPanelItems;
        context.CopyCommandLineSelection = CopyCommandLineSelection;
        context.PasteTextIntoCommandLine = PasteTextIntoCommandLine;
        context.MovePanelColumn = MovePanelColumn;
        context.OnVisibleCommandLineTextEdited = OnVisibleCommandLineTextEdited;
        context.CloseSearchResultsPanel = CloseSearchResultsPanel;
        context.ExecuteCommand = ExecuteCommand;
        context.EnsureActivePanelVisible = _panelWorkspace.EnsureActivePanelVisible;
        context.BrowseCommandHistory = BrowseCommandHistory;
        context.HideCommandCompletion = HideCommandCompletion;
        context.ResetCommandHistoryNavigation = ResetCommandHistoryNavigation;
        context.TryGoUp = TryGoUp;
        context.CanExecuteFunctionKeyCommand = CanExecuteFunctionKeyCommand;
    }

    private void BindMouseInputContext(MouseInputContext context)
    {
        context.ExecuteRegisteredCommand = ExecuteRegisteredCommand;
        context.CanExecuteFunctionKeyCommand = CanExecuteFunctionKeyCommand;
        context.PasteTextIntoCommandLine = PasteTextIntoCommandLine;
        context.ResetCommandHistoryNavigation = ResetCommandHistoryNavigation;
        context.HideCommandCompletion = HideCommandCompletion;
        context.SafeRefresh = SafeRefresh;
        context.OpenPanelItem = OpenPanelItem;
    }

    private void BindRenderContext(ApplicationRenderContext context)
    {
        context.BuildMenuDefinition = BuildMenuDefinition;
    }

    private void BindCallbacks(ApplicationServiceCallbacks callbacks)
    {
        callbacks.ActiveState = () => _panelWorkspace.ActiveState;
        callbacks.GetActiveSide = () => _panelWorkspace.ActiveSide;
        callbacks.SetActiveSide = _panelWorkspace.SetActiveSide;
        callbacks.SetQuickView = quickView => QuickView = quickView;
        callbacks.PanelOptions = () => PanelOptions;
        callbacks.GetPanelState = _panelWorkspace.GetPanelState;
        callbacks.PanelSideForState = _panelWorkspace.PanelSideForState;
        callbacks.VisibleRows = _panelWorkspace.VisibleRows;
        callbacks.VisibleRowsForSide = _panelWorkspace.VisibleRows;
        callbacks.StartWatching = StartWatching;
        callbacks.SafeRefresh = SafeRefresh;
        callbacks.ClosePanelQuickSearchForState = ClosePanelQuickSearchForState;
        callbacks.ClosePanelQuickSearchForPanel = ClosePanelQuickSearchForPanel;
        callbacks.HasVisiblePanels = () => _panelWorkspace.HasVisiblePanels;
        callbacks.IsPanelVisible = _panelWorkspace.IsPanelVisible;
        callbacks.ViewPanelFile = ViewPanelFile;
        callbacks.ExecuteInCurrentConsole = _externalConsoleCommandRunner.Execute;
        callbacks.CanExecuteFunctionKeyCommand = CanExecuteFunctionKeyCommand;
        callbacks.ExecuteMenuCommand = ExecuteMenuCommand;
        callbacks.IsRunning = () => _state.Running;
        callbacks.CaptureUnderlay = _terminalSurface.CaptureUnderlay;
        callbacks.StartWatchingInitialPanels = StartWatchingInitialPanels;
        callbacks.RestoreTerminal = _terminalSurface.RestoreTerminal;
        callbacks.HandleKeyInput = HandleRuntimeKeyInput;
        callbacks.HandleModifierInput = HandleRuntimeModifierInput;
        callbacks.HandleMouseInput = HandleRuntimeMouseInput;
        callbacks.HandleApplicationInput = HandleRuntimeApplicationInput;
        callbacks.RefreshPanels = RefreshPanels;
        callbacks.OpenModulePanel = OpenModulePanel;
    }

    internal ApplicationSession Session => _session;

    internal bool QuickView
    {
        get => _state.QuickView;
        set => _state.QuickView = value;
    }

    private AppSettingsAlias.PanelOptionsSettings PanelOptions => _settings.Panels.Options;

    public void Run()
    {
        _runtime.Run();
    }

    private void StartWatchingInitialPanels()
    {
        StartWatching(_left, PanelSide.Left);
        StartWatching(_right, PanelSide.Right);
    }

    private ApplicationRuntimeRenderRequest HandleRuntimeKeyInput(ConsoleKeyInfo key)
    {
        bool scrolledHiddenViewport = _terminalSurface.ScrollHiddenViewportToBottomForInput();
        bool functionKeyLayerChanged = SetFunctionKeyLayer(key.Modifiers);
        bool shouldRender = _keyboardInputRouter.Handle(key) || scrolledHiddenViewport || functionKeyLayerChanged;
        return new ApplicationRuntimeRenderRequest(shouldRender);
    }

    private ApplicationRuntimeRenderRequest HandleRuntimeModifierInput(ConsoleModifiers modifiers)
    {
        if (!_panelWorkspace.HasVisiblePanels)
            return ApplicationRuntimeRenderRequest.None;

        return new(SetFunctionKeyLayer(modifiers));
    }

    private ApplicationRuntimeRenderRequest HandleRuntimeMouseInput(MouseConsoleInputEvent mouseEvt)
    {
        bool scrolledHiddenViewport = _terminalSurface.ScrollHiddenViewportToBottomForInput();
        bool handled = _applicationSurface.HasCommittedFrame &&
            _mouseInputRouter.Handle(mouseEvt, _applicationSurface.CommittedFrame);
        bool shouldRender = handled || scrolledHiddenViewport;
        return new ApplicationRuntimeRenderRequest(shouldRender);
    }

    private ApplicationRuntimeRenderRequest HandleRuntimeApplicationInput(UiRoutedInput<ApplicationUiFrame> routed)
    {
        bool scrolledHiddenViewport = _terminalSurface.ScrollHiddenViewportToBottomForInput();
        ApplicationRuntimeRenderRequest request = _applicationInputDispatcher.Handle(routed);
        return new ApplicationRuntimeRenderRequest(request.ShouldRender || scrolledHiddenViewport);
    }

    private void Render() => _composition.Render();

    private void RenderCommandLineOnly() => _composition.Render();

    private bool SetFunctionKeyLayer(ConsoleModifiers modifiers)
    {
        var layer = FunctionKeyLayerResolver.ResolvePressedLayer(modifiers);
        if (_functionKeyLayer == layer)
            return false;

        _functionKeyLayer = layer;
        return true;
    }

    internal void ResetFunctionKeyLayer() => _functionKeyLayer = FunctionKeyLayer.Plain;

    private MenuBarDefinition BuildMenuDefinition() =>
        _menuProvider.BuildMenu(new MenuBuildContext
        {
            ActivePanelSide = _active,
            LeftPanel = _left,
            RightPanel = _right,
            LeftViewMode = _leftViewMode,
            RightViewMode = _rightViewMode,
            Settings = _settings,
            CanSaveSettings = _saveSettings is not null,
            ModuleMenuItems = _moduleCatalog.MenuItems,
        });

    // ── panel visibility ──────────────────────────────────────────────────────

    private void ClosePanelQuickSearch() =>
        _panelQuickSearch.Close();

    private void ClosePanelQuickSearchForPanel(PanelSide side)
        => _panelQuickSearch.CloseForPanel(side);

    private void ClosePanelQuickSearchForState(FilePanelState state) =>
        _panelQuickSearch.CloseForState(state);

    // ── Ctrl+O ────────────────────────────────────────────────────────────────

    private bool TogglePanels() =>
        _panelVisibility.TogglePanels();

    internal bool TogglePanelVisibility(PanelSide side) =>
        _panelVisibility.TogglePanel(side);

    // ── key handling ──────────────────────────────────────────────────────────

    internal FilePanelState ActiveState => _panelWorkspace.ActiveState;

    internal int VisibleRows() =>
        _panelWorkspace.VisibleRows();

    internal int VisibleRows(PanelSide side) =>
        _panelWorkspace.VisibleRows(side);

    private bool CopyCommandLineSelection()
    {
        string? selectedText = _cmdLine.SelectedText;
        if (!string.IsNullOrEmpty(selectedText))
            _clipboard.TrySetText(selectedText);

        return true;
    }

    private bool PasteTextIntoCommandLine()
    {
        if (!_clipboard.TryGetText(out string text) || string.IsNullOrEmpty(text))
            return true;

        string singleLine = text.ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrEmpty(singleLine))
            return true;

        _cmdLine.InsertText(singleLine);
        if (_panelWorkspace.HasVisiblePanels)
            OnVisibleCommandLineTextEdited();
        else
            ResetCommandHistoryNavigation();

        return true;
    }

    private bool CanExecuteFunctionKeyCommand(string commandId) =>
        _commandRegistry.CanExecute(commandId, _commandContext);

    private bool ExecuteRegisteredCommand(string commandId, object? args = null) =>
        _commandRegistry.Execute(commandId, _commandContext, args).ShouldRender;

    private void SelectAllCommandLineTextOrPanelItems()
    {
        if (_cmdLine.HasText)
            _cmdLine.SelectAll();
        else
            _ctrl.ToggleSelectAll(ActiveState, PanelOptions);
    }

    private void OnVisibleCommandLineTextEdited()
    {
        ResetCommandHistoryNavigation();
        _commandCompletion.TemporarilyHidden = false;
        RefreshCommandCompletion();
    }

    private void RefreshCommandCompletion()
    {
        _commandCompletionController.Refresh(
            _cmdLine,
            _panelWorkspace.HasVisiblePanels,
            HasCommandCompletionRows());
    }

    private bool HasCommandCompletionRows()
    {
        var size = LastRenderSizeOrCurrent();
        return CommandCompletionVisibleRows(size) > 0;
    }

    private ConsoleSize LastRenderSizeOrCurrent() =>
        _ui.LastRenderViewport?.Size ?? _screen.GetSize();

    private static int CommandCompletionVisibleRows(ConsoleSize size)
    {
        int rowsAboveCommandLine = ApplicationLayoutService.CommandLineRow(size) - 2;
        return Math.Max(0, Math.Min(MaxCommandCompletionRows, rowsAboveCommandLine));
    }

    internal void HideCommandCompletion(bool temporarily)
    {
        _commandCompletionController.Hide(temporarily);
    }

    private bool BrowseCommandHistory(int direction, CommandHistoryNavigationStart start)
    {
        _commandHistoryNavigator.Browse(_cmdLine, direction, start);
        HideCommandCompletion(temporarily: false);
        return true;
    }

    internal void ResetCommandHistoryNavigation()
    {
        _commandHistoryNavigator.Reset();
    }

    internal void ResetTransientNavigationUi()
    {
        ClosePanelQuickSearch();
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();
    }

    private void MovePanelColumn(int direction)
    {
        var geometry = _panelWorkspace.ActiveColumnGeometry();
        _ctrl.MoveCursorByColumn(
            ActiveState,
            direction,
            geometry.RowsPerColumn,
            geometry.ColumnCount,
            geometry.VisibleRows);
    }

    internal bool OpenTopMenu()
    {
        ClosePanelQuickSearch();
        _menuController.HandleKey(
            new ConsoleKeyInfo('\0', ConsoleKey.F9, shift: false, alt: false, control: false),
            BuildMenuDefinition(),
            _active);
        return true;
    }

    internal FilePanelState PassiveState => _active == PanelSide.Left ? _right : _left;

    internal void OpenSearchResultsPanel(
        FilePanelState state,
        SearchRequest request,
        IReadOnlyList<SearchResultItem> results,
        bool cancelled)
    {
        _searchResults.OpenPanel(state, request, results, cancelled);
    }

    private void CloseSearchResultsPanel(FilePanelState state, PanelSide side)
    {
        _searchResults.ClosePanel(state, side);
    }

    internal void GoToSearchResult(FilePanelState state, PanelSide side, SearchResultItem result)
    {
        _searchResults.GoToResult(state, side, result);
    }

    internal void GoToSearchResult(FilePanelState state, PanelSide side, FilePanelItem result)
    {
        _searchResults.GoToResult(state, side, result);
    }

    // ── F5 — copy ─────────────────────────────────────────────────────────────

    private ApplicationRuntimeRenderRequest ExecuteMenuCommand(MenuCommandRequest request)
    {
        var result = _commandRegistry.Execute(request.CommandId, _commandContext, request.Args);
        return new ApplicationRuntimeRenderRequest(result.ShouldRender);
    }

    internal FilePanelState GetPanelState(PanelSide side) =>
        side == PanelSide.Left ? _left : _right;

    internal ApplicationCommandResult OpenModuleDiskMenuItem(Guid actionId, PanelSide panelSide) =>
        _modulePanelOpener.OpenDiskMenuItem(actionId, panelSide);

    internal void OpenModulePanel(PanelSide panelSide, IModulePanel panel)
    {
        _modulePanelOpener.OpenPanel(panelSide, panel);
    }

    internal void ViewPanelFile(FilePanelState state, FilePanelItem item)
    {
        _panelFileViewer.ViewPanelFile(state, item);
    }

    // ── shell execution ───────────────────────────────────────────────────────

    internal void ExecuteCommand(string command) =>
        _commandLineCommandExecutor.Execute(command);

    internal void ExecuteInCurrentConsole(string workDir, string displayCommand, Action execute) =>
        _externalConsoleCommandRunner.Execute(workDir, displayCommand, execute);

    // ── navigation helpers ────────────────────────────────────────────────────

    internal void OpenPanelItem(FilePanelState state, PanelSide side, FilePanelItem item)
    {
        if (state.SearchRequest is not null)
        {
            GoToSearchResult(state, side, item);
            return;
        }

        if (item.IsDirectory)
        {
            OpenDirectoryItem(state, side, item);
            return;
        }

        OpenFileItem(item);
    }

    private void OpenDirectoryItem(FilePanelState state, PanelSide side, FilePanelItem item)
    {
        _panelNavigation.OpenDirectoryItem(state, side, item);
    }

    private void OpenFileItem(FilePanelItem item)
    {
        _panelFileOpener.OpenFileItem(ActiveState, item);
    }

    private void TryGoUp()
    {
        _panelNavigation.TryGoUp(ActiveState, _active);
    }

    internal void RefreshPanels()
    {
        _panelRefresh.RefreshPanels(_left, _right);
    }

    internal void RefreshPanelsAfterFileOperation()
    {
        _panelRefresh.RefreshPanelsAfterFileOperation(_left, _right);
    }

    private void RefreshSearchResultsPanel(FilePanelState state, int visibleRows)
    {
        _searchResults.RefreshPanel(state, visibleRows);
    }

    private PanelSide PanelSideForState(FilePanelState state) =>
        ReferenceEquals(state, _left) ? PanelSide.Left : PanelSide.Right;

    internal void SafeRefresh(FilePanelState state, int visibleRows)
    {
        _panelRefresh.SafeRefresh(state, visibleRows);
    }

    internal void SetPanelSortMode(FilePanelState state, SortMode mode, int visibleRows)
    {
        _panelSort.SetPanelSortMode(state, mode, visibleRows);
    }

    internal void SortVirtualPanel(FilePanelState state, string? keepCursorPath, int visibleRows)
    {
        _panelSort.SortVirtualPanel(state, keepCursorPath, visibleRows);
    }

    internal static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;

    // ── auto-refresh ──────────────────────────────────────────────────────────

    internal void StartWatching(FilePanelState state, PanelSide side)
    {
        _autoRefresh.StartWatching(state, side);
    }

}
