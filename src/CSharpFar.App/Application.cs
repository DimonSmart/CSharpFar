using CSharpFar.App.Dialogs;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Commands;
using CSharpFar.App.CommandLine;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.Files;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.HitTesting;
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
using CSharpFar.FileSystem;
using CSharpFar.Module.Abstractions;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;
using CSharpFar.FarNetHost;
using CSharpFar.Shell;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App;

public sealed class Application
{
    private const int MaxCommandCompletionRows = CommandHistoryCompletionRenderer.MaxVisibleRows;

    private readonly ScreenRenderer _screen;
    private readonly ITerminalScreenMode? _terminalScreenMode;
    private readonly ApplicationRuntime _runtime;
    private readonly IFileSystemService _fs;
    private readonly PanelController _ctrl;
    private readonly IShellService _shell;
    private readonly IFileLauncher _fileLauncher;
    private readonly IFileOperationService _fileOps;
    private readonly PanelFileViewerService _panelFileViewer;
    private readonly PanelFileOpener _panelFileOpener;
    private readonly PanelAutoRefreshService _autoRefresh;
    private readonly ApplicationPanelWorkspaceRenderer _panelWorkspaceRenderer;
    private readonly ClockRenderer _clockRenderer;
    private readonly ApplicationFunctionKeyBarRenderer _functionKeyBarRenderer;
    private readonly ApplicationOverlayRenderer _overlayRenderer;
    private readonly ApplicationCommandLineRenderer _commandLineRenderer;
    private readonly KeyboardInputContext _keyboardInputContext;
    private readonly KeyboardInputRouter _keyboardInputRouter;
    private readonly ShellUnderlayService _shellUnderlay;
    private readonly PanelRefreshService _panelRefresh;
    private readonly PanelSearchResultsService _searchResults;
    private readonly PanelSortServiceFacade _panelSort;
    private readonly PanelNavigationService _panelNavigation;
    private readonly ISearchService _searchService;
    private readonly FilePanelSourceRegistry _sourceRegistry;
    private readonly NativeModuleCatalog _moduleCatalog;
    private readonly ModulePanelOpener _modulePanelOpener;
    private readonly FarNetPanelActionService _farNetPanelActions;
    private readonly IHistoryStore _history;
    private readonly CommandHistoryNavigator _commandHistoryNavigator;
    private readonly CommandCompletionController _commandCompletionController;
    private readonly ChangeDirectoryCommandExecutor _changeDirectoryCommandExecutor;
    private readonly AppSettingsAlias _settings;
    private readonly UserMenuStore _userMenu;
    private readonly Action? _saveSettings;
    private readonly IVolumeService? _volumeService;
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
    private IFileHighlightService?          _highlightService;
    private MenuState _menuState => _session.Menu.State;
    private readonly DefaultMenuDefinitionProvider _menuProvider;
    private readonly DefaultFunctionKeyBindingProvider _functionKeyBindingProvider;
    private readonly ApplicationCommandRegistry _commandRegistry;
    private readonly ApplicationCommandContext _commandContext;
    private readonly MenuLayoutService      _menuLayoutService;
    private readonly TopMenuController      _menuController;
    private readonly IReadOnlyList<FunctionKeyBinding> _functionKeyBindings;
    private PanelItemClick? _lastLeftPanelItemClick
    {
        get => _session.Mouse.LastLeftPanelItemClick;
        set => _session.Mouse.LastLeftPanelItemClick = value;
    }
    private FunctionKeyLayer _functionKeyLayer
    {
        get => _session.FunctionKeyLayer;
        set => _session.FunctionKeyLayer = value;
    }
    private readonly QuickViewDirectorySizeController _quickViewDirectorySize;
    private bool _isCommandLineMouseSelecting
    {
        get => _session.Mouse.IsCommandLineSelecting;
        set => _session.Mouse.IsCommandLineSelecting = value;
    }

    private enum ConsoleViewportChange
    {
        None,
        OriginOnly,
        Size,
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
        FarNetModuleHost?            farNetModuleHost  = null,
        bool                         enableBuiltInNetworkModules = true,
        string?                      configDirectory   = null,
        ITextClipboard?              clipboard         = null,
        ITerminalScreenMode?         terminalScreenMode = null)
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
            farNetModuleHost,
            enableBuiltInNetworkModules,
            configDirectory,
            clipboard,
            terminalScreenMode))
    {
    }

    internal Application(ApplicationServices services)
    {
        _screen = services.Screen;
        _terminalScreenMode = services.TerminalScreenMode;
        _fs = services.FileSystem;
        _sourceRegistry = services.SourceRegistry;
        _ctrl = services.PanelController;
        _shell = services.Shell;
        _fileLauncher = services.FileLauncher;
        _fileOps = services.FileOperations;
        _searchService = services.SearchService;
        _history = services.History;
        _commandHistoryNavigator = services.CommandHistoryNavigator;
        _commandCompletionController = services.CommandCompletionController;
        _settings = services.Settings;
        _clipboard = services.Clipboard;
        _userMenu = services.UserMenu;
        _saveSettings = services.SaveSettings;
        _volumeService = services.VolumeService;
        _session = services.Session;
        _menuProvider = services.MenuProvider;
        _functionKeyBindingProvider = services.FunctionKeyBindingProvider;
        _functionKeyBindings = services.FunctionKeyBindings;
        _menuLayoutService = services.MenuLayoutService;
        _highlightService = services.HighlightService;
        _changeDirectoryCommandExecutor = services.ChangeDirectoryCommandExecutor;
        _menuController = services.MenuController;
        _autoRefresh = services.AutoRefresh;
        _panelWorkspaceRenderer = services.PanelWorkspaceRenderer;
        _clockRenderer = services.ClockRenderer;
        _panelSort = services.PanelSort;
        _panelNavigation = services.PanelNavigation;
        _searchResults = services.SearchResults;
        _panelRefresh = services.PanelRefresh;
        _panelQuickSearch = services.PanelQuickSearch;
        _panelFileViewer = services.PanelFileViewer;
        _panelFileOpener = services.PanelFileOpener;
        _moduleCatalog = services.ModuleCatalog;
        _modulePanelOpener = services.ModulePanelOpener;
        _farNetPanelActions = services.FarNetPanelActions;
        _commandRegistry = services.CommandRegistry;
        _commandContext   = new ApplicationCommandContext(this);
        _functionKeyBarRenderer = services.FunctionKeyBarRenderer;
        _overlayRenderer = services.OverlayRenderer;
        _commandLineRenderer = services.CommandLineRenderer;
        _keyboardInputContext = services.KeyboardInputContext;
        _keyboardInputRouter = services.KeyboardInputRouter;
        _shellUnderlay = services.ShellUnderlay;
        _quickViewDirectorySize = services.QuickViewDirectorySize;
        BindCallbacks(services.Callbacks);
        BindKeyboardInputContext(_keyboardInputContext);
        _runtime = services.Runtime;

    }

    private void BindKeyboardInputContext(KeyboardInputContext context)
    {
        context.BuildMenuDefinition = BuildMenuDefinition;
        context.TogglePanels = TogglePanels;
        context.TryHandleFarNetPanelShortcut = TryHandleFarNetPanelShortcut;
        context.ExecuteRegisteredCommand = ExecuteRegisteredCommand;
        context.SelectAllCommandLineTextOrPanelItems = SelectAllCommandLineTextOrPanelItems;
        context.CopyCommandLineSelection = CopyCommandLineSelection;
        context.PasteTextIntoCommandLine = PasteTextIntoCommandLine;
        context.MovePanelColumn = MovePanelColumn;
        context.OnVisibleCommandLineTextEdited = OnVisibleCommandLineTextEdited;
        context.TryHideCommandCompletionTemporarily = TryHideCommandCompletionTemporarily;
        context.CloseSearchResultsPanel = CloseSearchResultsPanel;
        context.TryAcceptCommandCompletion = TryAcceptCommandCompletion;
        context.ExecuteCommand = ExecuteCommand;
        context.EnsureActivePanelVisible = EnsureActivePanelVisible;
        context.TryMoveCommandCompletionSelection = TryMoveCommandCompletionSelection;
        context.BrowseCommandHistory = BrowseCommandHistory;
        context.HideCommandCompletion = HideCommandCompletion;
        context.ResetCommandHistoryNavigation = ResetCommandHistoryNavigation;
        context.TryGoUp = TryGoUp;
        context.CanExecuteFunctionKeyCommand = CanExecuteFunctionKeyCommand;
    }

    private void BindCallbacks(ApplicationServiceCallbacks callbacks)
    {
        callbacks.ActiveState = () => ActiveState;
        callbacks.GetActiveSide = () => ActiveSide;
        callbacks.SetActiveSide = side => ActiveSide = side;
        callbacks.SetQuickView = quickView => QuickView = quickView;
        callbacks.PanelOptions = () => PanelOptions;
        callbacks.GetPanelState = GetPanelState;
        callbacks.PanelSideForState = PanelSideForState;
        callbacks.VisibleRows = VisibleRows;
        callbacks.VisibleRowsForSide = VisibleRows;
        callbacks.StartWatching = StartWatching;
        callbacks.SafeRefresh = SafeRefresh;
        callbacks.ClosePanelQuickSearchForState = ClosePanelQuickSearchForState;
        callbacks.ClosePanelQuickSearchForPanel = ClosePanelQuickSearchForPanel;
        callbacks.HasVisiblePanels = () => HasVisiblePanels;
        callbacks.IsPanelVisible = IsPanelVisible;
        callbacks.ViewPanelFile = ViewPanelFile;
        callbacks.ExecuteInCurrentConsole = ExecuteInCurrentConsole;
        callbacks.CanExecuteFunctionKeyCommand = CanExecuteFunctionKeyCommand;
        callbacks.ExecuteMenuCommand = ExecuteMenuCommand;
        callbacks.IsRunning = () => _state.Running;
        callbacks.CaptureUnderlay = _shellUnderlay.Capture;
        callbacks.StartWatchingInitialPanels = StartWatchingInitialPanels;
        callbacks.RenderUntilStable = RenderUntilStable;
        callbacks.RenderCommandLineOnlyUntilStable = RenderCommandLineOnlyUntilStable;
        callbacks.RestoreHiddenScreen = () => _shellUnderlay.RestoreForHiddenScreen(HasVisiblePanels);
        callbacks.RestoreTerminal = RestoreTerminal;
        callbacks.HandleResizeInput = HandleRuntimeResizeInput;
        callbacks.CheckViewportAfterInput = CheckRuntimeViewportAfterInput;
        callbacks.HandleKeyInput = HandleRuntimeKeyInput;
        callbacks.HandleModifierInput = HandleRuntimeModifierInput;
        callbacks.HandleMouseInput = HandleRuntimeMouseInput;
        callbacks.RefreshPanels = RefreshPanels;
        callbacks.OpenModulePanel = OpenModulePanel;
    }

    internal ScreenRenderer CommandScreen => _screen;

    internal ApplicationSession Session => _session;

    internal PanelController CommandPanelController => _ctrl;

    internal IFileLauncher CommandFileLauncher => _fileLauncher;

    internal IFileOperationService CommandFileOperations => _fileOps;

    internal ISearchService CommandSearchService => _searchService;

    internal IHistoryStore CommandHistory => _history;

    internal UserMenuStore CommandUserMenu => _userMenu;

    internal ITextClipboard CommandClipboard => _clipboard;

    internal AppSettingsAlias CommandSettings => _settings;

    internal IVolumeService? CommandVolumeService => _volumeService;

    internal IReadOnlyList<ModuleMenuProjection> ModuleDiskMenuItems =>
        _moduleCatalog.DiskMenuItems;

    internal FilePanelState CommandLeftPanel => _left;

    internal FilePanelState CommandRightPanel => _right;

    internal CommandLineState CommandLine => _cmdLine;

    internal bool CanSaveSettings => _saveSettings is not null;

    internal ConsolePalette CommandPalette
    {
        get => _state.Palette;
        set => _state.Palette = value;
    }

    internal PanelSide ActiveSide
    {
        get => _active;
        set => SetActiveSide(value);
    }

    internal bool Running
    {
        get => _state.Running;
        set => _state.Running = value;
    }

    internal bool QuickView
    {
        get => _state.QuickView;
        set => _state.QuickView = value;
    }

    internal PanelViewMode LeftViewMode
    {
        get => _leftViewMode;
        set => _leftViewMode = value;
    }

    internal PanelViewMode RightViewMode
    {
        get => _rightViewMode;
        set => _rightViewMode = value;
    }

    internal IFileHighlightService? HighlightService
    {
        get => _highlightService;
        set => _highlightService = value;
    }

    internal AppSettingsAlias.PanelOptionsSettings PanelOptions => _settings.Panels.Options;

    internal void SaveSettings() => _saveSettings?.Invoke();

    public void Run()
    {
        _runtime.Run();
    }

    private void StartWatchingInitialPanels()
    {
        StartWatching(_left, PanelSide.Left);
        StartWatching(_right, PanelSide.Right);
    }

    private ApplicationRuntimeRenderRequest HandleRuntimeResizeInput()
    {
        var viewportChange = GetConsoleViewportChange();
        return !AcceptHiddenViewportScroll(viewportChange) &&
            viewportChange != ConsoleViewportChange.None
            ? new ApplicationRuntimeRenderRequest(ShouldRender: true, IsResize: true)
            : ApplicationRuntimeRenderRequest.None;
    }

    private ApplicationRuntimeRenderRequest CheckRuntimeViewportAfterInput() =>
        HandleRuntimeResizeInput();

    private ApplicationRuntimeRenderRequest HandleRuntimeKeyInput(ConsoleKeyInfo key)
    {
        bool scrolledHiddenViewport = ScrollHiddenViewportToBottomForInput();
        bool functionKeyLayerChanged = SetFunctionKeyLayer(key.Modifiers);
        bool shouldRender = _keyboardInputRouter.Handle(key) || scrolledHiddenViewport || functionKeyLayerChanged;
        return new ApplicationRuntimeRenderRequest(shouldRender, IsResize: false);
    }

    private ApplicationRuntimeRenderRequest HandleRuntimeModifierInput(ConsoleModifiers modifiers) =>
        new(SetFunctionKeyLayer(modifiers), IsResize: false);

    private ApplicationRuntimeRenderRequest HandleRuntimeMouseInput(MouseConsoleInputEvent mouseEvt)
    {
        bool scrolledHiddenViewport = ScrollHiddenViewportToBottomForInput();
        bool shouldRender = HandleMouse(mouseEvt) || scrolledHiddenViewport;
        return new ApplicationRuntimeRenderRequest(shouldRender, IsResize: false);
    }

    // ── quick view dir size ───────────────────────────────────────────────────

    private void UpdateQuickViewDirSize()
    {
        var item = _active == PanelSide.Left ? _ctrl.CurrentItem(_left) : _ctrl.CurrentItem(_right);
        _quickViewDirectorySize.Update(_state.QuickView, item);
    }

    // ── rendering ─────────────────────────────────────────────────────────────

    private void Render()
    {
        UpdateQuickViewDirSize();
        ApplyTerminalScreenMode();
        if (!UsesTerminalScreenMode && HasHiddenPanels)
            _shellUnderlay.RestoreForHiddenScreen(HasVisiblePanels);

        _screen.SetRenderingOutputMode(true);
        using var frame = _screen.BeginFrame();
        _screen.SetCursorVisible(false);

        var viewport = _screen.FrameViewport;
        var size   = viewport.Size;
        _ui.LastRenderViewport = viewport;
        var panelBounds = _panelWorkspaceRenderer.Render(
            size,
            _left,
            _right,
            _active,
            _leftViewMode,
            _rightViewMode,
            _state.QuickView,
            _quickViewDirectorySize.CurrentState,
            IsPanelVisible);
        int panelH = panelBounds.PanelHeight;
        _ui.LeftBounds = panelBounds.Left;
        _ui.RightBounds = panelBounds.Right;

        if (HasVisiblePanels)
            new DirectoryShortcutBarRenderer(_screen, _state.Palette)
                .Render(panelH - 1, size.Width, _settings.DirectoryShortcuts);

        if (IsPanelVisible(PanelSide.Right))
            RenderClock(size);

        _commandLineRenderer.Render(panelH, size, ActiveState.CurrentDirectory, _cmdLine);
        _overlayRenderer.RenderCommandCompletion(size, panelH, _commandCompletion);

        RenderFunctionKeyBar(size);

        _overlayRenderer.RenderMenuOverlay(size, BuildMenuDefinition(), _menuState);

        if (_menuState.OpenState == MenuOpenState.Closed)
        {
            if (_panelQuickSearch.State is not null)
            {
                if (!_overlayRenderer.RenderPanelQuickSearch(
                        _panelQuickSearch.State,
                        _ui.LeftBounds,
                        _ui.RightBounds,
                        IsPanelVisible))
                {
                    _screen.SetCursorVisible(false);
                }
            }
            else
            {
                _commandLineRenderer.PositionCursor(panelH, size, ActiveState.CurrentDirectory, _cmdLine);
            }
        }
        else
            _screen.SetCursorVisible(false);
    }

    private void RenderCommandLineOnly()
    {
        ApplyTerminalScreenMode();
        _screen.SetRenderingOutputMode(true);
        using var frame = _screen.BeginFrame();

        var viewport = _screen.FrameViewport;
        var size = viewport.Size;
        _ui.LastRenderViewport = viewport;

        int row = ApplicationLayoutService.CommandLineRow(size);
        _commandLineRenderer.Render(row, size, ActiveState.CurrentDirectory, _cmdLine);
        _commandLineRenderer.PositionCursor(row, size, ActiveState.CurrentDirectory, _cmdLine);
    }

    private void RenderCommandLineOnlyUntilStable()
    {
        while (_state.Running)
        {
            RenderCommandLineOnly();
            if (!_screen.FrameWasInterrupted)
            {
                _screen.DrainResizeEvents();
                break;
            }
        }
    }

    private void RenderClock(ConsoleSize size)
    {
        _clockRenderer.Render(size);
    }

    private void RenderFunctionKeyBar(ConsoleSize size)
    {
        _functionKeyBarRenderer.Render(size, _functionKeyLayer);
    }

    private ConsoleViewportChange GetConsoleViewportChange()
    {
        if (!_ui.LastRenderViewport.HasValue)
            return ConsoleViewportChange.None;

        var viewport = _screen.GetViewport();
        var last = _ui.LastRenderViewport.Value;
        if (viewport == last)
            return ConsoleViewportChange.None;

        return viewport.Width == last.Width && viewport.Height == last.Height
            ? ConsoleViewportChange.OriginOnly
            : ConsoleViewportChange.Size;
    }

    private bool AcceptHiddenViewportScroll(ConsoleViewportChange viewportChange)
    {
        if (HasVisiblePanels || viewportChange != ConsoleViewportChange.OriginOnly)
            return false;

        _ui.LastRenderViewport = _screen.GetViewport();
        return true;
    }

    private bool ScrollHiddenViewportToBottomForInput()
    {
        if (HasVisiblePanels)
            return false;

        bool scrolled = _screen.TryScrollViewportToBottom();
        if (!scrolled)
            return false;

        _shellUnderlay.Capture();
        _ui.LastRenderViewport = _shellUnderlay.CapturedViewport ?? _screen.GetViewport();
        return scrolled;
    }

    private bool UsesTerminalScreenMode =>
        _terminalScreenMode?.IsSupported == true;

    private void ApplyTerminalScreenMode()
    {
        if (UsesTerminalScreenMode)
        {
            if (HasVisiblePanels)
                _terminalScreenMode!.EnsureApplicationScreen();
            else
                _terminalScreenMode!.EnsureMainScreen();
            return;
        }

        _shellUnderlay.ApplyLegacyConsoleScrollbackMode(HasVisiblePanels);
    }

    private void EnterHiddenMainScreenAtBottom()
    {
        ApplyTerminalScreenMode();

        if (UsesTerminalScreenMode)
        {
            _screen.TryScrollViewportToBottom();
            SyncRendererWithCurrentMainScreenViewport();
            return;
        }

        _shellUnderlay.RestoreForHiddenScreen(HasVisiblePanels);
    }

    private void PrepareMainScreenForExternalCommand()
    {
        if (UsesTerminalScreenMode)
        {
            _terminalScreenMode!.EnsureMainScreen();
            _screen.TryScrollViewportToBottom();
            SyncRendererWithCurrentMainScreenViewport();
            return;
        }

        _screen.SetConsoleScrollbackEnabled(true);
    }

    private void SyncRendererWithCurrentMainScreenViewport()
    {
        _shellUnderlay.Capture();
        _ui.LastRenderViewport = _shellUnderlay.CapturedViewport ?? _screen.GetViewport();
    }

    private void RestoreTerminal() =>
        _terminalScreenMode?.RestoreTerminal();

    /// <summary>
    /// Renders the screen, retrying if the console was resized mid-frame.
    /// Loops until a complete, uninterrupted frame is flushed.
    /// </summary>
    private void RenderUntilStable()
    {
        int attempt = 0;
        while (_state.Running)
        {
            attempt++;
            Render();
            if (!_screen.FrameWasInterrupted)
            {
                _screen.DrainResizeEvents();
                break;
            }
        }
    }

    private static bool IsResizeEvent(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.NoName &&
        key.KeyChar == '\0' &&
        key.Modifiers == 0;

    private bool SetFunctionKeyLayer(ConsoleModifiers modifiers)
    {
        var layer = FunctionKeyLayerResolver.ResolvePressedLayer(modifiers);
        if (_functionKeyLayer == layer)
            return false;

        _functionKeyLayer = layer;
        return true;
    }

    internal void ResetFunctionKeyLayer() => _functionKeyLayer = FunctionKeyLayer.Plain;

    private static bool IsTopMenuActivationMouse(MouseConsoleInputEvent evt) =>
        evt.Y == 0 &&
        evt.Button == MouseButton.Left &&
        (evt.Kind == MouseEventKind.Down || evt.Kind == MouseEventKind.Click);

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

    private bool HasHiddenPanels => _state.HiddenPanels != HiddenPanels.None;

    private bool HasVisiblePanels => _state.HiddenPanels != HiddenPanels.Both;

    internal bool CommandHasVisiblePanels => HasVisiblePanels;

    private bool IsPanelVisible(PanelSide side) =>
        (_state.HiddenPanels & HiddenPanelFlag(side)) == 0;

    private static HiddenPanels HiddenPanelFlag(PanelSide side) =>
        side == PanelSide.Left ? HiddenPanels.Left : HiddenPanels.Right;

    private static PanelSide OtherPanelSide(PanelSide side) =>
        side == PanelSide.Left ? PanelSide.Right : PanelSide.Left;

    private void SetActiveSide(PanelSide side)
    {
        if (_active == side)
            return;

        _panelQuickSearch.Close();
        _active = side;
    }

    private void EnsureActivePanelVisible()
    {
        if (IsPanelVisible(_active))
            return;

        var otherSide = OtherPanelSide(_active);
        if (IsPanelVisible(otherSide))
            SetActiveSide(otherSide);
    }

    private void ClosePanelQuickSearch() =>
        _panelQuickSearch.Close();

    private void ClosePanelQuickSearchForPanel(PanelSide side)
        => _panelQuickSearch.CloseForPanel(side);

    private void ClosePanelQuickSearchForState(FilePanelState state) =>
        _panelQuickSearch.CloseForState(state);

    // ── Ctrl+O ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Toggles panel visibility.
    /// Hide: restores the last captured underlay so the user sees shell output.
    /// Show: Render() will be called by the main loop.
    /// </summary>
    private bool TogglePanels()
    {
        ClosePanelQuickSearch();
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();
        _ui.PanelScrollbarDrag = null;

        if (_state.HiddenPanels == HiddenPanels.Both)
        {
            _state.HiddenPanels = HiddenPanels.None;
            _screen.TryScrollViewportToBottom();
            _ui.LastRenderViewport = _screen.GetViewport();
            ApplyTerminalScreenMode();
            return true;
        }

        _state.HiddenPanels = HiddenPanels.Both;
        EnterHiddenMainScreenAtBottom();
        _screen.SetCursorVisible(true);
        RenderCommandLineOnlyUntilStable();
        return false;
    }

    internal bool TogglePanelVisibility(PanelSide side)
    {
        ClosePanelQuickSearch();
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();
        _ui.PanelScrollbarDrag = null;

        var flag = HiddenPanelFlag(side);
        bool wasHidden = (_state.HiddenPanels & flag) != 0;

        if (wasHidden)
        {
            _state.HiddenPanels &= ~flag;
            _screen.TryScrollViewportToBottom();
            _ui.LastRenderViewport = _screen.GetViewport();
        }
        else
        {
            _state.HiddenPanels |= flag;
        }

        EnsureActivePanelVisible();

        if (_state.HiddenPanels == HiddenPanels.Both)
        {
            EnterHiddenMainScreenAtBottom();
            _screen.SetCursorVisible(true);
            RenderCommandLineOnlyUntilStable();
            return false;
        }

        ApplyTerminalScreenMode();
        return true;
    }

    // ── key handling ──────────────────────────────────────────────────────────

    internal FilePanelState ActiveState => _active == PanelSide.Left ? _left : _right;

    private PanelViewMode ActiveViewMode =>
        _active == PanelSide.Left ? _leftViewMode : _rightViewMode;

    internal int VisibleRows()
    {
        return VisibleRows(ActiveViewMode);
    }

    internal int VisibleRows(PanelSide side)
    {
        var mode = side == PanelSide.Left ? _leftViewMode : _rightViewMode;
        return VisibleRows(mode);
    }

    private int VisibleRows(PanelViewMode mode)
    {
        var size   = _screen.GetSize();
        int panelH = ApplicationLayoutService.PanelHeight(size);
        var bounds = new Rect(0, 0, 0, panelH);
        return mode == PanelViewMode.BriefTwoColumns
            ? BriefTwoColumnsPanelRenderer.VisibleRows(bounds, PanelOptions)
            : PanelRenderer.VisibleRows(bounds, PanelOptions);
    }

    private (int RowsPerColumn, int ColumnCount, int VisibleRows) ActiveColumnGeometry()
    {
        var mode = ActiveViewMode;
        int visibleRows = VisibleRows(mode);

        if (mode != PanelViewMode.BriefTwoColumns)
            return (Math.Max(1, visibleRows), 1, visibleRows);

        var size = _screen.GetSize();
        var bounds = new Rect(0, 0, 0, ApplicationLayoutService.PanelHeight(size));
        int rowsPerColumn = BriefTwoColumnsPanelRenderer.RowsPerColumn(bounds, PanelOptions);
        return (rowsPerColumn, 2, visibleRows);
    }

    // ── mouse handling ────────────────────────────────────────────────────────

    private bool HandleMouse(MouseConsoleInputEvent evt)
    {
        if (_panelQuickSearch.State is not null)
            ClosePanelQuickSearch();

        if (TryHandleFunctionKeyBarMouse(evt))
            return true;

        if (TryHandleCommandLineMouse(evt))
            return true;

        if (!HasVisiblePanels)
            return false;

        if (TryHandleDirectoryShortcutBarMouse(evt))
            return true;

        if (_menuState.OpenState != MenuOpenState.Closed || IsTopMenuActivationMouse(evt))
        {
            var definition = BuildMenuDefinition();
            var size = _screen.GetSize();
            var layout = _menuLayoutService.CalculateLayout(
                new Rect(0, 0, size.Width, size.Height),
                definition,
                _menuState);
            return _menuController.HandleMouse(evt, definition, layout, _active);
        }

        if (TryHandleCommandCompletionScrollbarMouse(evt))
            return true;

        if (TryHandleCommandCompletionItemMouse(evt))
            return true;

        if (TryHandlePanelScrollbarDrag(evt))
            return true;

        // Identify which panel was hit
        bool inLeft  = IsPanelVisible(PanelSide.Left)  && _ui.LeftBounds.Contains(evt.X,  evt.Y);
        bool inRight = IsPanelVisible(PanelSide.Right) && _ui.RightBounds.Contains(evt.X, evt.Y);
        if (!inLeft && !inRight)
        {
            ClearPanelItemClickOnMousePress(evt);
            return false;
        }

        var side  = inLeft ? PanelSide.Left : PanelSide.Right;
        var state = inLeft ? _left : _right;
        var mode  = inLeft ? _leftViewMode : _rightViewMode;
        var bounds = inLeft ? _ui.LeftBounds : _ui.RightBounds;
        int visRows = VisibleRows(side);

        if (_state.QuickView && side != _active)
        {
            ClearPanelItemClickOnMousePress(evt);
            return false;
        }

        if (TryHandlePanelScrollbarMouse(evt, side, state, mode, bounds, visRows))
            return true;

        if (evt.Button == MouseButton.Left &&
            (evt.Kind == MouseEventKind.Down || evt.Kind == MouseEventKind.Click) &&
            PanelErrorRenderer.HitTestRetry(evt.X, evt.Y, bounds, state, mode, PanelOptions))
        {
            SetActiveSide(side);
            SafeRefresh(state, visRows);
            _lastLeftPanelItemClick = null;
            return true;
        }

        // Mouse wheel: scroll the panel under cursor
        if (evt.Kind == MouseEventKind.Wheel)
        {
            SetActiveSide(side);
            int delta = evt.Button == MouseButton.WheelUp ? -3 : 3;
            _ctrl.ScrollView(state, delta, visRows);
            return true;
        }

        // Right click: activate panel, move cursor, optionally toggle selection
        if (evt.Button == MouseButton.Right && evt.Kind == MouseEventKind.Down)
        {
            _lastLeftPanelItemClick = null;
            SetActiveSide(side);
            int? itemIdx = HitTestPanelItemForMouse(evt, side, bounds, state, mode);
            if (itemIdx.HasValue)
            {
                _ctrl.SetCursorTo(state, itemIdx.Value, visRows);
                if (PanelOptions.RightClickSelectsFiles)
                {
                    var item = state.Items[itemIdx.Value];
                    if (PanelController.CanSelect(item, PanelOptions))
                        _ctrl.ToggleCurrentSelection(state, PanelOptions);
                }
            }
            return true;
        }

        if (evt.Button == MouseButton.Left && evt.Kind == MouseEventKind.DoubleClick)
        {
            SetActiveSide(side);
            int? itemIdx = HitTestPanelItemForMouse(evt, side, bounds, state, mode);
            if (itemIdx.HasValue)
            {
                _ctrl.SetCursorTo(state, itemIdx.Value, visRows);

                var item = state.Items[itemIdx.Value];
                var currentClick = new PanelItemClick(side, itemIdx.Value, item.FullPath);
                if (_lastLeftPanelItemClick == currentClick)
                    OpenPanelItem(state, side, item);
            }

            _lastLeftPanelItemClick = null;
            return true;
        }

        // Left click: activate panel and move cursor
        if (evt.Button == MouseButton.Left &&
            (evt.Kind == MouseEventKind.Down || evt.Kind == MouseEventKind.Click))
        {
            SetActiveSide(side);
            int? itemIdx = HitTestPanelItemForMouse(evt, side, bounds, state, mode);
            if (itemIdx.HasValue)
            {
                _ctrl.SetCursorTo(state, itemIdx.Value, visRows);
                var item = state.Items[itemIdx.Value];
                _lastLeftPanelItemClick = new PanelItemClick(side, itemIdx.Value, item.FullPath);
            }
            else
            {
                _lastLeftPanelItemClick = null;
            }
            return true;
        }

        return false;
    }

    private bool TryHandleFunctionKeyBarMouse(MouseConsoleInputEvent evt)
    {
        if (evt.Button != MouseButton.Left ||
            evt.Kind is not (MouseEventKind.Down or MouseEventKind.Click))
        {
            return false;
        }

        var size = LastRenderSizeOrCurrent();
        if (!FunctionKeyBarRenderer.TryGetKeyNumberAt(evt, size.Height - 1, size.Width, out int keyNumber))
        {
            return false;
        }

        var binding = _functionKeyBindings.FirstOrDefault(candidate =>
            candidate.Layer == _functionKeyLayer &&
            candidate.KeyNumber == keyNumber &&
            CanExecuteFunctionKeyCommand(candidate.CommandId));

        return binding is not null && ExecuteRegisteredCommand(binding.CommandId);
    }

    private bool TryHandleDirectoryShortcutBarMouse(MouseConsoleInputEvent evt)
    {
        var size = LastRenderSizeOrCurrent();
        if (!DirectoryShortcutBarRenderer.TryGetShortcutNumberAt(
                evt,
                ApplicationLayoutService.PanelHeight(size) - 1,
                size.Width,
                _settings.DirectoryShortcuts,
                out int number))
        {
            return false;
        }

        return ExecuteRegisteredCommand(
            DirectoryShortcutCommandIds.Navigate,
            new NavigateToDirectoryShortcutArgs(number));
    }

    private bool TryHandleCommandLineMouse(MouseConsoleInputEvent evt)
    {
        var size = LastRenderSizeOrCurrent();
        int row = ApplicationLayoutService.CommandLineRow(size);
        bool isSelectionDrag = _isCommandLineMouseSelecting &&
            evt.Button == MouseButton.Left &&
            evt.Kind == MouseEventKind.Move;

        if (evt.Y != row && !isSelectionDrag)
            return false;

        if (evt.Button == MouseButton.Left &&
            evt.Kind is MouseEventKind.Down or MouseEventKind.Click)
        {
            _cmdLine.MoveCursorTo(CommandLineTextPositionFromMouseX(size, evt.X));
            _isCommandLineMouseSelecting = evt.Kind == MouseEventKind.Down;
            ResetCommandHistoryNavigation();
            return true;
        }

        if (evt.Button == MouseButton.Left && evt.Kind == MouseEventKind.DoubleClick)
        {
            SelectCommandLineWordAt(CommandLineTextPositionFromMouseX(size, evt.X));
            _isCommandLineMouseSelecting = false;
            ResetCommandHistoryNavigation();
            return true;
        }

        if (isSelectionDrag)
        {
            _cmdLine.MoveCursorWithSelection(CommandLineTextPositionFromMouseX(size, evt.X));
            ResetCommandHistoryNavigation();
            return true;
        }

        if (evt.Button == MouseButton.Left && evt.Kind == MouseEventKind.Up)
        {
            _isCommandLineMouseSelecting = false;
            return true;
        }

        if (evt.Button == MouseButton.Right &&
            evt.Kind is MouseEventKind.Down or MouseEventKind.Click)
        {
            PasteTextIntoCommandLine();
            return true;
        }

        return false;
    }

    private int CommandLineTextPositionFromMouseX(ConsoleSize size, int mouseX)
    {
        if (size.Width <= 0)
            return 0;

        string prompt = ActiveState.CurrentDirectory + ">";
        int fullLength = prompt.Length + _cmdLine.Text.Length;
        int offset = GetCommandLineDisplayOffset(size.Width, prompt.Length, fullLength, _cmdLine.CursorPosition);
        int x = Math.Clamp(mouseX, 0, size.Width - 1);
        return Math.Clamp(x + offset - prompt.Length, 0, _cmdLine.Text.Length);
    }

    private static int GetCommandLineDisplayOffset(
        int totalWidth,
        int promptLength,
        int fullLength,
        int cursorPosition)
    {
        if (fullLength < totalWidth)
            return 0;

        int rawCursorX = promptLength + cursorPosition;
        int maxOffset = Math.Max(0, fullLength - totalWidth + 1);
        return Math.Clamp(rawCursorX - totalWidth + 1, 0, maxOffset);
    }

    private void SelectCommandLineWordAt(int position)
    {
        string text = _cmdLine.Text;
        if (text.Length == 0)
            return;

        position = Math.Clamp(position, 0, text.Length - 1);
        if (char.IsWhiteSpace(text[position]) && position > 0 && !char.IsWhiteSpace(text[position - 1]))
            position--;

        int start = position;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;

        int end = position;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;

        _cmdLine.MoveCursorTo(start);
        _cmdLine.MoveCursorWithSelection(end);
    }

    private int? HitTestPanelItemForMouse(
        MouseConsoleInputEvent evt,
        PanelSide side,
        Rect bounds,
        FilePanelState state,
        PanelViewMode mode)
    {
        int x = evt.X;

        // Before panels had separate frames, the first usable right-panel column was
        // where the new right-panel left border is now. Keep mouse targeting tolerant.
        if (side == PanelSide.Right && x == bounds.X)
            x++;

        return PanelHitTester.HitTestItem(x, evt.Y, bounds, state, mode, PanelOptions);
    }

    private void ClearPanelItemClickOnMousePress(MouseConsoleInputEvent evt)
    {
        if (evt.Kind is MouseEventKind.Down or MouseEventKind.Click or MouseEventKind.DoubleClick)
            _lastLeftPanelItemClick = null;
    }

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
        if (HasVisiblePanels)
            OnVisibleCommandLineTextEdited();
        else
            ResetCommandHistoryNavigation();

        return true;
    }

    private bool TryHandleFarNetPanelShortcut(ConsoleKeyInfo key)
    {
        return _farNetPanelActions.TryHandleShortcut(ActiveState, _cmdLine.HasText, key);
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

    private bool TryHandlePanelScrollbarDrag(MouseConsoleInputEvent evt)
    {
        if (_ui.PanelScrollbarDrag is not { } drag)
            return false;

        var state = GetPanelState(drag.Side);
        int firstVisibleIndex = state.ScrollOffset;
        ScrollBarDragState? dragState = drag.DragState;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                evt,
                drag.DragState.Bounds,
                drag.DragState.TotalItems,
                drag.DragState.ViewportItems,
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        _ui.PanelScrollbarDrag = dragState.HasValue
            ? new PanelScrollbarDrag(drag.Side, dragState.Value)
            : null;

        SetActiveSide(drag.Side);
        _ctrl.ScrollView(state, firstVisibleIndex - state.ScrollOffset, drag.DragState.ViewportItems);
        _lastLeftPanelItemClick = null;
        return true;
    }

    private bool TryHandlePanelScrollbarMouse(
        MouseConsoleInputEvent evt,
        PanelSide side,
        FilePanelState state,
        PanelViewMode mode,
        Rect bounds,
        int visibleRows)
    {
        if (!TryGetPanelScrollbarBounds(bounds, mode, out var scrollbarBounds))
            return false;

        int firstVisibleIndex = state.ScrollOffset;
        ScrollBarDragState? dragState = null;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                evt,
                scrollbarBounds,
                state.Items.Count,
                visibleRows,
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        _ui.PanelScrollbarDrag = dragState.HasValue
            ? new PanelScrollbarDrag(side, dragState.Value)
            : null;

        SetActiveSide(side);
        _ctrl.ScrollView(state, firstVisibleIndex - state.ScrollOffset, visibleRows);
        _lastLeftPanelItemClick = null;
        return true;
    }

    private bool TryGetPanelScrollbarBounds(Rect bounds, PanelViewMode mode, out Rect scrollbarBounds)
    {
        if (mode == PanelViewMode.BriefTwoColumns)
        {
            int rowsPerColumn = BriefTwoColumnsPanelRenderer.RowsPerColumn(bounds, PanelOptions);
            scrollbarBounds = new Rect(bounds.Right - 1, bounds.Y + 2, 1, rowsPerColumn);
            return rowsPerColumn > 0;
        }

        int visibleRows = PanelRenderer.VisibleRows(bounds, PanelOptions);
        scrollbarBounds = new Rect(bounds.Right - 1, bounds.Y + 1, 1, visibleRows);
        return visibleRows > 0;
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
            HasVisiblePanels,
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

    private bool TryHandleCommandCompletionScrollbarMouse(MouseConsoleInputEvent evt)
    {
        if (!_commandCompletion.Visible && !_commandCompletion.ScrollbarDrag.HasValue)
            return false;

        var size = LastRenderSizeOrCurrent();
        int visibleRows = CommandCompletionVisibleRows(size);
        if (visibleRows <= 0 || _commandCompletion.Matches.Count <= visibleRows)
            return false;

        int height = visibleRows + 2;
        int commandLineRow = ApplicationLayoutService.CommandLineRow(size);
        var scrollbarBounds = new Rect(size.Width - 1, commandLineRow - height + 1, 1, visibleRows);
        int firstVisibleIndex = _commandCompletion.FirstVisibleIndex;
        var dragState = _commandCompletion.ScrollbarDrag;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                evt,
                scrollbarBounds,
                _commandCompletion.Matches.Count,
                visibleRows,
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        _commandCompletion.ScrollbarDrag = dragState;
        _commandCompletion.FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            firstVisibleIndex,
            _commandCompletion.Matches.Count,
            visibleRows);
        _commandCompletionController.ClampSelectionToViewport(visibleRows);
        return true;
    }

    private bool TryHandleCommandCompletionItemMouse(MouseConsoleInputEvent evt)
    {
        if (!_commandCompletion.Visible ||
            _commandCompletion.Matches.Count == 0 ||
            evt.Button != MouseButton.Left ||
            evt.Kind is not (MouseEventKind.Down or MouseEventKind.Click or MouseEventKind.DoubleClick))
        {
            return false;
        }

        var size = LastRenderSizeOrCurrent();
        int visibleRows = CommandCompletionVisibleRows(size);
        if (visibleRows <= 0)
            return false;

        int rowCount = Math.Min(visibleRows, _commandCompletion.Matches.Count);
        int height = rowCount + 2;
        int commandLineRow = ApplicationLayoutService.CommandLineRow(size);
        var contentBounds = new Rect(
            1,
            commandLineRow - height + 1,
            Math.Max(0, size.Width - 2),
            rowCount);

        if (evt.X < contentBounds.X ||
            evt.X >= contentBounds.Right ||
            evt.Y < contentBounds.Y ||
            evt.Y >= contentBounds.Bottom)
        {
            return false;
        }

        int itemIndex = _commandCompletion.FirstVisibleIndex + evt.Y - contentBounds.Y;
        if (itemIndex < 0 || itemIndex >= _commandCompletion.Matches.Count)
            return false;

        _commandCompletion.SelectedIndex = itemIndex;
        if (IsCommandCompletionNeutralSelected())
        {
            HideCommandCompletion(temporarily: false);
            return true;
        }

        _cmdLine.SetText(_commandCompletion.Matches[_commandCompletion.SelectedIndex]);
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();
        return true;
    }

    private bool TryMoveCommandCompletionSelection(int delta)
    {
        return _commandCompletionController.TryMoveSelection(
            delta,
            CommandCompletionVisibleRows(LastRenderSizeOrCurrent()));
    }

    private bool TryAcceptCommandCompletion()
    {
        if (!_commandCompletion.Visible || _commandCompletion.Matches.Count == 0 || !HasCommandCompletionRows())
            return false;

        if (IsCommandCompletionNeutralSelected())
        {
            HideCommandCompletion(temporarily: false);
            ResetCommandHistoryNavigation();
            return false;
        }

        _cmdLine.SetText(_commandCompletion.Matches[_commandCompletion.SelectedIndex]);
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();
        return true;
    }

    private bool IsCommandCompletionNeutralSelected() =>
        _commandCompletionController.IsNeutralSelected;

    private bool TryHideCommandCompletionTemporarily()
    {
        if (!_commandCompletion.Visible)
            return false;

        HideCommandCompletion(temporarily: true);
        return true;
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
        var geometry = ActiveColumnGeometry();
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

    internal FileOperationOptions BuildFileOperationOptions() =>
        FileOperationOptionsFactory.Create(_settings);

    private MenuCommandResult ExecuteMenuCommand(MenuCommandRequest request)
    {
        return _commandRegistry
            .Execute(request.CommandId, _commandContext, request.Args)
            .ToMenuCommandResult();
    }

    internal FilePanelState GetPanelState(PanelSide side) =>
        side == PanelSide.Left ? _left : _right;

    internal ApplicationCommandResult OpenModuleMenuItem(Guid actionId) =>
        _modulePanelOpener.OpenMenuItem(actionId, _active);

    internal ApplicationCommandResult OpenModuleDiskMenuItem(Guid actionId, PanelSide panelSide) =>
        _modulePanelOpener.OpenDiskMenuItem(actionId, panelSide);

    internal void OpenModulePanel(PanelSide panelSide, IModulePanel panel)
    {
        _modulePanelOpener.OpenPanel(panelSide, panel);
    }

    internal string CombinePanelPath(FilePanelState state, string name)
    {
        if (state.SourceId == PanelSourceId.Local)
            return Path.Combine(state.SourcePath, name);

        string directory = state.SourcePath.TrimEnd('/');
        return directory.Length == 0 || directory == "/"
            ? "/" + name
            : directory + "/" + name;
    }

    internal void ViewPanelFile(FilePanelState state, FilePanelItem item)
    {
        _panelFileViewer.ViewPanelFile(state, item);
    }

    // ── shell execution ───────────────────────────────────────────────────────

    internal void ExecuteCommand(string command)
    {
        string workDir = ActiveState.CurrentDirectory;
        ClosePanelQuickSearch();
        _cmdLine.Clear();
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();
        AddCommandHistory(command, workDir);

        if (_modulePanelOpener.TryOpenFromCommandLine(command, _active))
            return;

        if (_changeDirectoryCommandExecutor.TryExecute(command))
            return;

        ExecuteInCurrentConsole(workDir, command, () => _shell.Execute(command, workDir));
    }

    private void AddCommandHistory(string command, string workingDirectory)
    {
        _history.AddCommand(new CommandHistoryItem
        {
            Command          = command,
            WorkingDirectory = workingDirectory,
        });
    }

    internal void ExecuteInCurrentConsole(string workDir, string displayCommand, Action execute)
    {
        HiddenPanels hiddenPanelsAfterCommand = _state.HiddenPanels;

        ShowShellUnderlayForCommand();
        PrintExecutedCommandPrompt(workDir, displayCommand);

        try
        {
            using var childConsoleMode = _screen.EnterChildProcessConsoleMode();
            execute();
        }
        finally
        {
            _screen.RestoreApplicationInputMode();
            MoveShellOutputAbovePromptArea();
            PrintInputPrompt(workDir);

            // Capture shell output NOW, before Render() paints panels over it.
            // This snapshot is what Ctrl+O will restore.
            _shellUnderlay.Capture();

            RefreshPanels();
            _state.HiddenPanels = hiddenPanelsAfterCommand;
            ApplyTerminalScreenMode();
            // Stable rendering is called by the main loop.
        }
    }

    private void MoveShellOutputAbovePromptArea()
    {
        var size = _screen.GetSize();
        if (size.Width <= 0 || size.Height <= 0)
            return;

        int cursorRow = SysConsole.CursorTop - SysConsole.WindowTop;
        if (cursorRow < ApplicationLayoutService.CommandLineRow(size))
            return;

        _screen.SetRenderingOutputMode(false);
        SysConsole.ResetColor();
        SysConsole.WriteLine();
        SysConsole.WriteLine();
    }

    private void ShowShellUnderlayForCommand()
    {
        PrepareMainScreenForExternalCommand();
        _screen.SetRenderingOutputMode(false);
        if (!UsesTerminalScreenMode)
            _shellUnderlay.RestoreOrClearVisibleArea();

        SysConsole.ResetColor();
        SysConsole.CursorVisible = true;
    }

    private void PrintExecutedCommandPrompt(string workDir, string command)
    {
        var size = _screen.GetSize();
        if (size.Width <= 0 || size.Height <= 0)
            return;

        int row = ApplicationLayoutService.CommandLineRow(size);
        ClearShellPromptArea(size);

        int x = WriteShellText(0, row, workDir + ">", ConsoleColor.White);
        WriteShellText(x, row, command, ConsoleColor.Yellow);

        SysConsole.ResetColor();

        int outputRow = Math.Min(size.Height - 1, row + 1);
        SysConsole.SetCursorPosition(0, SysConsole.WindowTop + outputRow);
    }

    private void PrintInputPrompt(string workDir)
    {
        _screen.SetRenderingOutputMode(true);

        var size = _screen.GetSize();
        if (size.Width <= 0 || size.Height <= 0)
            return;

        ClearShellPromptArea(size);

        int row = ApplicationLayoutService.CommandLineRow(size);
        _commandLineRenderer.Render(row, size, workDir, _cmdLine);
        _commandLineRenderer.PositionCursor(row, size, workDir, _cmdLine);
    }

    private void ClearShellPromptArea(ConsoleSize size)
    {
        int commandRow = ApplicationLayoutService.CommandLineRow(size);
        _screen.FillRegion(new Rect(0, commandRow, size.Width, 1), CellStyle.Default);

        int bottomRow = size.Height - 1;
        if (bottomRow != commandRow)
            _screen.FillRegion(new Rect(0, bottomRow, size.Width, 1), CellStyle.Default);
    }

    private int WriteShellText(int x, int y, string text, ConsoleColor foreground)
    {
        var size = _screen.GetSize();
        if (x >= size.Width || y >= size.Height)
            return x;

        int len = Math.Min(text.Length, size.Width - x);
        if (len <= 0)
            return x;

        var style = new CellStyle(foreground, ConsoleColor.Black);
        _screen.Write(x, y, text.AsSpan(0, len), style);
        return x + len;
    }

    // ── navigation helpers ────────────────────────────────────────────────────

    internal void OpenPanelItem(FilePanelState state, PanelSide side, FilePanelItem item)
    {
        if (state.SearchRequest is not null)
        {
            GoToSearchResult(state, side, item);
            return;
        }

        if (TryOpenFarNetPanelItem(state, side, item))
            return;

        if (item.IsDirectory)
        {
            OpenDirectoryItem(state, side, item);
            return;
        }

        OpenFileItem(item);
    }

    internal bool TryEditFarNetPanelItem(FilePanelState state, FilePanelItem item)
    {
        return _farNetPanelActions.TryEditItem(state, item);
    }

    private bool TryOpenFarNetPanelItem(FilePanelState state, PanelSide side, FilePanelItem item)
    {
        return _farNetPanelActions.TryOpenItem(state, side, item);
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

    internal ConsoleKeyInfo? TryReadConsoleKeyForCommand() =>
        TryReadConsoleKey(out var key) ? key : null;

    internal void ShowReadOnlyPanelMessage(string action)
    {
        new MessageDialog(_screen, _state.Palette).Show(
            action,
            "The current panel source does not support this operation.");
    }

    // ── auto-refresh ──────────────────────────────────────────────────────────

    internal void StartWatching(FilePanelState state, PanelSide side)
    {
        _autoRefresh.StartWatching(state, side);
    }

    // ── alias to avoid namespace conflict with CSharpFar.Console ─────────────
    private static class SysConsole
    {
        public static int WindowTop
        {
            get
            {
                try { return global::System.Console.WindowTop; }
                catch (Exception ex) when (IsConsoleStateException(ex)) { return 0; }
            }
        }

        public static int CursorTop
        {
            get
            {
                try { return global::System.Console.CursorTop; }
                catch (Exception ex) when (IsConsoleStateException(ex)) { return 0; }
            }
        }

        public static bool CursorVisible
        {
            set
            {
                try { global::System.Console.CursorVisible = value; }
                catch (Exception ex) when (IsConsoleStateException(ex)) { }
            }
        }

        public static void ResetColor()
        {
            try { global::System.Console.ResetColor(); }
            catch (Exception ex) when (IsConsoleStateException(ex)) { }
        }

        public static void SetCursorPosition(int x, int y) =>
            TrySetCursorPosition(x, y);

        public static void WriteLine()
        {
            try { global::System.Console.WriteLine(); }
            catch (Exception ex) when (IsConsoleStateException(ex)) { }
        }

        private static void TrySetCursorPosition(int x, int y)
        {
            try { global::System.Console.SetCursorPosition(x, y); }
            catch (Exception ex) when (IsConsoleStateException(ex)) { }
        }

        private static bool IsConsoleStateException(Exception ex) =>
            ex is IOException or
                  InvalidOperationException or
                  ArgumentOutOfRangeException or
                  PlatformNotSupportedException;
    }

}
