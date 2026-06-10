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

    private readonly FilePanelState _left;
    private readonly FilePanelState _right;
    private readonly CommandLineState _cmdLine;
    private readonly CommandCompletionState _commandCompletion;
    private readonly PanelQuickSearchController _panelQuickSearch;

    private PanelSide     _active        = PanelSide.Left;
    private readonly ApplicationState _state;
    private readonly UiTransientState _ui;
    private PanelViewMode           _leftViewMode;
    private PanelViewMode           _rightViewMode;
    private IFileHighlightService?          _highlightService;
    private readonly MenuState              _menuState;
    private readonly DefaultMenuDefinitionProvider _menuProvider;
    private readonly DefaultFunctionKeyBindingProvider _functionKeyBindingProvider;
    private readonly ApplicationCommandRegistry _commandRegistry;
    private readonly ApplicationCommandContext _commandContext;
    private readonly MenuLayoutService      _menuLayoutService;
    private readonly TopMenuController      _menuController;
    private readonly IReadOnlyList<FunctionKeyBinding> _functionKeyBindings;
    private PanelItemClick?                 _lastLeftPanelItemClick;
    private FunctionKeyLayer                _functionKeyLayer = FunctionKeyLayer.Plain;
    private readonly QuickViewDirectorySizeController _quickViewDirectorySize;
    private bool                             _isCommandLineMouseSelecting;

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
        ITextClipboard?              clipboard         = null)
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
            clipboard))
    {
    }

    internal Application(ApplicationServices services)
    {
        _screen = services.Screen;
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
        _state = services.State;
        _ui = services.Ui;
        _left = services.LeftPanel;
        _right = services.RightPanel;
        _cmdLine = services.CommandLine;
        _commandCompletion = services.CommandCompletion;
        _menuState = services.MenuState;
        _menuProvider = services.MenuProvider;
        _functionKeyBindingProvider = services.FunctionKeyBindingProvider;
        _functionKeyBindings = services.FunctionKeyBindings;
        _menuLayoutService = services.MenuLayoutService;
        _leftViewMode = services.LeftViewMode;
        _rightViewMode = services.RightViewMode;
        _highlightService = services.HighlightService;
        _changeDirectoryCommandExecutor = new ChangeDirectoryCommandExecutor(
            _ctrl,
            () => ActiveState,
            () => _active,
            () => PanelOptions,
            StartWatching);
        _menuController   = new TopMenuController(_menuState, ExecuteMenuCommand);
        _autoRefresh = new PanelAutoRefreshService(
            services.ChangeWatcher,
            services.LocationService,
            () => PanelOptions,
            GetPanelState,
            VisibleRows,
            SafeRefresh);
        _panelWorkspaceRenderer = new ApplicationPanelWorkspaceRenderer(
            _screen,
            () => _state.Palette,
            _ctrl,
            () => _highlightService,
            () => PanelOptions);
        _clockRenderer = new ClockRenderer(_screen, () => _state.Palette);
        _panelSort = new PanelSortServiceFacade(
            _ctrl,
            () => PanelOptions,
            ClosePanelQuickSearchForState);
        _panelNavigation = new PanelNavigationService(
            _ctrl,
            _history,
            () => PanelOptions,
            VisibleRows,
            ClosePanelQuickSearchForPanel,
            StartWatching);
        _searchResults = new PanelSearchResultsService(
            _screen,
            _searchService,
            () => _state.Palette,
            _ctrl,
            _history,
            () => PanelOptions,
            PanelSideForState,
            VisibleRows,
            ClosePanelQuickSearchForState,
            ClosePanelQuickSearchForPanel,
            StartWatching,
            _panelSort.SortVirtualPanel);
        _panelRefresh = new PanelRefreshService(
            _ctrl,
            () => PanelOptions,
            VisibleRows,
            ClosePanelQuickSearchForState,
            _searchResults.RefreshPanel);
        _panelQuickSearch = new PanelQuickSearchController(
            _ctrl,
            () => _active,
            () => HasVisiblePanels,
            IsPanelVisible,
            GetPanelState,
            VisibleRows);
        _panelFileViewer = new PanelFileViewerService(
            _screen,
            () => _state.Palette,
            _sourceRegistry,
            _history,
            _clipboard,
            _settings,
            _ctrl,
            PanelSideForState,
            VisibleRows,
            SafeRefresh);
        _panelFileOpener = new PanelFileOpener(
            _fileLauncher,
            _screen,
            () => _state.Palette,
            ViewPanelFile,
            ExecuteInCurrentConsole);
        var moduleUiServices = new ModuleUiServices
        {
            Screen = _screen,
            Palette = () => _state.Palette,
        };
        services.FarNetModuleHost?.Initialize(new FarNetModuleHostServices
        {
            Ui = moduleUiServices,
            DataRoot = Path.Combine(services.ConfigDirectory, "FarNet"),
            GetActivePanelSide = () => ActiveSide,
            GetPanelState = GetPanelState,
        });
        _moduleCatalog = ModuleCatalogFactory.Create(
            services.EnableBuiltInNetworkModules ? services.SftpModule ?? new SftpModule() : null,
            services.EnableBuiltInNetworkModules ? services.FtpModule ?? new FtpModule() : null,
            services.FarNetModuleHost,
            new ModuleStartupInfo
            {
                Ui = moduleUiServices,
                Settings = new ModuleSettingsService(services.ConfigDirectory),
                Credentials = services.CredentialStore,
                Panels = new ApplicationModulePanelHost(this),
            });
        _modulePanelOpener = new ModulePanelOpener(
            _moduleCatalog,
            _sourceRegistry,
            _ctrl,
            _screen,
            () => _state.Palette,
            () => PanelOptions,
            GetPanelState,
            side => ActiveSide = side,
            quickView => QuickView = quickView);
        _farNetPanelActions = new FarNetPanelActionService(
            _sourceRegistry,
            _ctrl,
            _screen,
            () => _state.Palette,
            _settings,
            _clipboard,
            _modulePanelOpener,
            VisibleRows,
            PanelSideForState,
            SafeRefresh);
        _commandRegistry = ApplicationCommandRegistry.CreateDefault();
        _commandContext   = new ApplicationCommandContext(this);
        _functionKeyBarRenderer = new ApplicationFunctionKeyBarRenderer(
            _screen,
            () => _state.Palette,
            _functionKeyBindings,
            CanExecuteFunctionKeyCommand);
        _overlayRenderer = new ApplicationOverlayRenderer(
            _screen,
            () => _state.Palette,
            _menuLayoutService);
        _commandLineRenderer = new ApplicationCommandLineRenderer(_screen, () => _state.Palette);
        _shellUnderlay = new ShellUnderlayService(_screen);
        _quickViewDirectorySize = new QuickViewDirectorySizeController(_autoRefresh.WakeInputLoop);
        _runtime = new ApplicationRuntime(
            _screen,
            new ApplicationRuntimeContext
            {
                IsRunning = () => _state.Running,
                HasVisiblePanels = () => HasVisiblePanels,
                WaitToken = () => _autoRefresh.WaitToken,
                CaptureUnderlay = _shellUnderlay.Capture,
                StartWatchingInitialPanels = StartWatchingInitialPanels,
                RenderUntilStable = RenderUntilStable,
                RenderCommandLineOnlyUntilStable = RenderCommandLineOnlyUntilStable,
                RestoreHiddenScreen = () => _shellUnderlay.RestoreForHiddenScreen(HasVisiblePanels),
                ResetWaitToken = _autoRefresh.ResetWaitToken,
                ProcessPendingRefreshes = _autoRefresh.ProcessPendingRefreshes,
                DisposeRuntimeState = _quickViewDirectorySize.Dispose,
                HandleResizeInput = HandleRuntimeResizeInput,
                CheckViewportAfterInput = CheckRuntimeViewportAfterInput,
                HandleKeyInput = HandleRuntimeKeyInput,
                HandleModifierInput = HandleRuntimeModifierInput,
                HandleMouseInput = HandleRuntimeMouseInput,
            });

    }

    internal ScreenRenderer CommandScreen => _screen;

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
        bool shouldRender = HandleKey(key) || scrolledHiddenViewport || functionKeyLayerChanged;
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
        _shellUnderlay.ApplyConsoleScrollbackMode(HasVisiblePanels);
        if (HasHiddenPanels)
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
        _shellUnderlay.ApplyConsoleScrollbackMode(HasVisiblePanels);
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

    private static bool IsPlainControlKey(ConsoleKeyInfo key, ConsoleKey consoleKey, char controlChar)
    {
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasAlt     = (key.Modifiers & ConsoleModifiers.Alt)     != 0;
        bool hasShift   = (key.Modifiers & ConsoleModifiers.Shift)   != 0;

        return !hasAlt && !hasShift &&
               ((hasControl && key.Key == consoleKey) ||
                key.KeyChar == controlChar);
    }

    private static bool IsPlainControlEnter(ConsoleKeyInfo key) =>
        HasOnlyControlModifier(key) && key.Key == ConsoleKey.Enter;

    private static bool IsPlainControlOpenBracket(ConsoleKeyInfo key) =>
        IsPlainControlBracket(key, ConsoleKey.Oem4, '[', '\u001b');

    private static bool IsPlainControlCloseBracket(ConsoleKeyInfo key) =>
        IsPlainControlBracket(key, ConsoleKey.Oem6, ']', '\u001d');

    private static bool IsPlainControlBackslash(ConsoleKeyInfo key) =>
        HasOnlyControlModifier(key) &&
        (key.Key == ConsoleKey.Oem5 || key.KeyChar == '\u001c');

    private static bool IsPlainControlBracket(
        ConsoleKeyInfo key,
        ConsoleKey consoleKey,
        char printableChar,
        char controlChar)
    {
        if (!HasOnlyControlModifier(key))
            return false;

        return key.Key == consoleKey ||
               key.KeyChar == printableChar ||
               (key.Key != ConsoleKey.Escape && key.KeyChar == controlChar);
    }

    private static bool HasOnlyControlModifier(ConsoleKeyInfo key)
    {
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasAlt     = (key.Modifiers & ConsoleModifiers.Alt)     != 0;
        bool hasShift   = (key.Modifiers & ConsoleModifiers.Shift)   != 0;

        return hasControl && !hasAlt && !hasShift;
    }

    private static string QuoteCommandLineInsertion(string text) =>
        text.Contains(' ') ? $"\"{text}\"" : text;

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
            _shellUnderlay.ApplyConsoleScrollbackMode(HasVisiblePanels);
            return true;
        }

        _state.HiddenPanels = HiddenPanels.Both;
        _shellUnderlay.ApplyConsoleScrollbackMode(HasVisiblePanels);
        _screen.SetCursorVisible(true);
        _shellUnderlay.RestoreForHiddenScreen(HasVisiblePanels);
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
        _shellUnderlay.ApplyConsoleScrollbackMode(HasVisiblePanels);

        if (_state.HiddenPanels == HiddenPanels.Both)
        {
            _screen.SetCursorVisible(true);
            _shellUnderlay.RestoreForHiddenScreen(HasVisiblePanels);
            RenderCommandLineOnlyUntilStable();
            return false;
        }

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

    private bool HandleKey(ConsoleKeyInfo key)
    {
        if (_menuState.OpenState != MenuOpenState.Closed)
        {
            if (!HasVisiblePanels)
            {
                _menuController.Close();
                return true;
            }

            return _menuController.HandleKey(key, BuildMenuDefinition(), _active);
        }

        var quickSearchResult = _panelQuickSearch.HandleKey(key);
        if (quickSearchResult == PanelQuickSearchKeyResult.Handled)
            return true;

        // Ctrl+O: toggle panels — check before printable-char routing
        if (IsPlainControlKey(key, ConsoleKey.O, '\u000f'))
            return TogglePanels();

        if (TryHandleFarCommandLineShortcut(key))
            return true;

        if (!HasVisiblePanels)
            return HandleHiddenCommandLineKey(key);

        if (TryHandleFarNetPanelShortcut(key))
            return true;

        if (TryHandleDirectoryShortcut(key))
            return true;

        // Ctrl+S: settings dialog
        if (IsPlainControlKey(key, ConsoleKey.S, '\u0013'))
            return ExecuteRegisteredCommand(MenuCommandIds.SettingsOpenPanelSettings);

        // Ctrl+\: navigate active panel to drive root
        if (IsPlainControlBackslash(key))
            return ExecuteRegisteredCommand(ApplicationCommandIds.NavigateToRoot);

        // Alt+1 / Alt+2: view mode for active panel
        if ((key.Modifiers & ConsoleModifiers.Alt) != 0 &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Shift)) == 0)
        {
            if (key.Key == ConsoleKey.D1 || key.Key == ConsoleKey.NumPad1)
            {
                return ExecuteRegisteredCommand(
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs
                    {
                        PanelSide = _active,
                        ViewMode = PanelViewMode.Full,
                    });
            }

            if (key.Key == ConsoleKey.D2 || key.Key == ConsoleKey.NumPad2)
            {
                return ExecuteRegisteredCommand(
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs
                    {
                        PanelSide = _active,
                        ViewMode = PanelViewMode.BriefTwoColumns,
                    });
            }
        }

        // Ctrl+Q: toggle quick view
        if (IsPlainControlKey(key, ConsoleKey.Q, '\u0011'))
        {
            _state.QuickView = !_state.QuickView;
            return true;
        }

        if (IsPlainControlKey(key, ConsoleKey.A, '\u0001'))
        {
            SelectAllCommandLineTextOrPanelItems();
            return true;
        }

        if (IsPlainControlKey(key, ConsoleKey.C, '\u0003'))
            return CopyCommandLineSelection();

        if (IsPlainControlKey(key, ConsoleKey.V, '\u0016'))
            return PasteTextIntoCommandLine();

        if (TryHandleCommandLineNavigationKey(key, forceCommandLine: false))
            return true;

        // Ctrl+* - invert selection
        bool isControlShortcut =
            (key.Modifiers & ConsoleModifiers.Control) != 0 &&
            (key.Modifiers & ConsoleModifiers.Alt) == 0;
        if (isControlShortcut)
        {
            switch (key.Key)
            {
                case ConsoleKey.Multiply:
                    _ctrl.InvertSelection(ActiveState, PanelOptions);
                    return true;
                case ConsoleKey.D8 when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                    _ctrl.InvertSelection(ActiveState, PanelOptions);
                    return true;
            }
        }

        if (TryHandleFunctionKey(key, out bool functionKeyShouldRender))
            return functionKeyShouldRender;

        if (_panelQuickSearch.TryStart(key))
        {
            HideCommandCompletion(temporarily: false);
            ResetCommandHistoryNavigation();
            return true;
        }

        int vr = VisibleRows();

        switch (key.Key)
        {
            // ── Horizontal navigation / command line editing ──────────────────
            case ConsoleKey.LeftArrow:
                if (_cmdLine.HasText || _cmdLine.HasSelection)
                {
                    _cmdLine.MoveCursor(-1);
                    return true;
                }

                MovePanelColumn(-1);
                return true;

            case ConsoleKey.RightArrow:
                if (_cmdLine.HasText || _cmdLine.HasSelection)
                {
                    _cmdLine.MoveCursor(+1);
                    return true;
                }

                MovePanelColumn(+1);
                return true;

            case ConsoleKey.Home:
                if (_cmdLine.HasText || _cmdLine.HasSelection) _cmdLine.MoveToStart();
                else _ctrl.MoveToFirst(ActiveState);
                return true;

            case ConsoleKey.End:
                if (_cmdLine.HasText || _cmdLine.HasSelection) _cmdLine.MoveToEnd();
                else _ctrl.MoveToLast(ActiveState, vr);
                return true;

            case ConsoleKey.Delete:
                _cmdLine.DeleteForward();
                OnVisibleCommandLineTextEdited();
                return true;

            case ConsoleKey.Backspace:
                bool hadCommandText = _cmdLine.HasText;
                if (hadCommandText)
                {
                    _cmdLine.DeleteBack();
                    OnVisibleCommandLineTextEdited();
                }
                else
                {
                    HideCommandCompletion(temporarily: false);
                    TryGoUp();
                }
                return true;

            case ConsoleKey.Escape:
                if (TryHideCommandCompletionTemporarily())
                    return true;

                if (ActiveState.SearchRequest is not null)
                {
                    CloseSearchResultsPanel(ActiveState, _active);
                    return true;
                }

                _cmdLine.Clear();
                HideCommandCompletion(temporarily: false);
                return true;

            // ── Execution ─────────────────────────────────────────────────────
            case ConsoleKey.Enter:
                if (TryAcceptCommandCompletion())
                    return true;

                if (_cmdLine.HasText) ExecuteCommand(_cmdLine.Text);
                else ExecuteRegisteredCommand(ApplicationCommandIds.OpenCurrentItem);
                return true;

            // ── Selection ────────────────────────────────────────────────────
            case ConsoleKey.Insert:
                _ctrl.ToggleSelection(ActiveState, vr, PanelOptions);
                return true;

            // ── Panel navigation ──────────────────────────────────────────────
            case ConsoleKey.Tab:
                var otherSide = OtherPanelSide(_active);
                if (IsPanelVisible(otherSide))
                    SetActiveSide(otherSide);
                else
                    EnsureActivePanelVisible();
                return true;

            case ConsoleKey.UpArrow:
                if (TryMoveCommandCompletionSelection(-1))
                    return true;

                _ctrl.MoveCursor(ActiveState, -1, vr);
                return true;

            case ConsoleKey.DownArrow:
                if (TryMoveCommandCompletionSelection(+1))
                    return true;

                _ctrl.MoveCursor(ActiveState, +1, vr);
                return true;

            case ConsoleKey.PageUp:
                _ctrl.MoveCursor(ActiveState, -vr, vr);
                return true;

            case ConsoleKey.PageDown:
                _ctrl.MoveCursor(ActiveState, +vr, vr);
                return true;

        }

        // Printable characters always go to the command line. This must run
        // after special keys so malformed function-key chars cannot be inserted.
        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (isPrintable)
        {
            _cmdLine.Insert(key.KeyChar);
            OnVisibleCommandLineTextEdited();
            return true;
        }

        return false;
    }

    private bool TryHandleDirectoryShortcut(ConsoleKeyInfo key)
    {
        // ConsoleKeyInfo does not preserve left/right Ctrl identity. The Win32
        // input layer can distinguish it, but the logical key contract cannot.
        // Keep this as Ctrl+number until that contract carries the side.
        if ((key.Modifiers & ConsoleModifiers.Control) == 0 ||
            (key.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Shift)) != 0)
        {
            return false;
        }

        int? number = key.Key switch
        {
            ConsoleKey.D0 or ConsoleKey.NumPad0 => 0,
            ConsoleKey.D1 or ConsoleKey.NumPad1 => 1,
            ConsoleKey.D2 or ConsoleKey.NumPad2 => 2,
            ConsoleKey.D3 or ConsoleKey.NumPad3 => 3,
            ConsoleKey.D4 or ConsoleKey.NumPad4 => 4,
            ConsoleKey.D5 or ConsoleKey.NumPad5 => 5,
            ConsoleKey.D6 or ConsoleKey.NumPad6 => 6,
            ConsoleKey.D7 or ConsoleKey.NumPad7 => 7,
            ConsoleKey.D8 or ConsoleKey.NumPad8 => 8,
            ConsoleKey.D9 or ConsoleKey.NumPad9 => 9,
            _ => null,
        };

        return number is not null &&
            ExecuteRegisteredCommand(
                DirectoryShortcutCommandIds.Navigate,
                new NavigateToDirectoryShortcutArgs(number.Value));
    }

    private bool TryHandleCommandLineNavigationKey(ConsoleKeyInfo key, bool forceCommandLine)
    {
        bool hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        if (hasAlt)
            return false;

        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        bool shouldUseCommandLine = forceCommandLine || _cmdLine.HasText || _cmdLine.HasSelection || hasControl || hasShift;

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow when shouldUseCommandLine:
                if (hasControl && hasShift)
                    _cmdLine.MoveToPreviousWordWithSelection();
                else if (hasControl)
                    _cmdLine.MoveToPreviousWord();
                else if (hasShift)
                    _cmdLine.MoveCursorWithSelection(_cmdLine.CursorPosition - 1);
                else
                    _cmdLine.MoveCursor(-1);
                ResetCommandHistoryNavigation();
                return true;

            case ConsoleKey.RightArrow when shouldUseCommandLine:
                if (hasControl && hasShift)
                    _cmdLine.MoveToNextWordWithSelection();
                else if (hasControl)
                    _cmdLine.MoveToNextWord();
                else if (hasShift)
                    _cmdLine.MoveCursorWithSelection(_cmdLine.CursorPosition + 1);
                else
                    _cmdLine.MoveCursor(+1);
                ResetCommandHistoryNavigation();
                return true;

            case ConsoleKey.Home when shouldUseCommandLine:
                if (hasShift)
                    _cmdLine.MoveCursorWithSelection(0);
                else
                    _cmdLine.MoveToStart();
                ResetCommandHistoryNavigation();
                return true;

            case ConsoleKey.End when shouldUseCommandLine:
                if (hasShift)
                    _cmdLine.MoveCursorWithSelection(_cmdLine.Text.Length);
                else
                    _cmdLine.MoveToEnd();
                ResetCommandHistoryNavigation();
                return true;

            default:
                return false;
        }
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

    private bool TryHandleFunctionKey(ConsoleKeyInfo key, out bool shouldRender)
    {
        shouldRender = false;

        if (key.Key is < ConsoleKey.F1 or > ConsoleKey.F12)
            return false;

        if (!FunctionKeyLayerResolver.TryResolveChordLayer(key.Modifiers, out var layer))
            return false;

        var binding = _functionKeyBindings.FirstOrDefault(candidate =>
            candidate.Layer == layer &&
            candidate.Key == key.Key);

        if (binding is null)
            return false;

        if (!CanExecuteFunctionKeyCommand(binding.CommandId) && !binding.RunsWhenUnavailable)
        {
            shouldRender = true;
            return true;
        }

        shouldRender = ExecuteRegisteredCommand(binding.CommandId);
        return true;
    }

    private bool TryHandlePanelVisibilityFunctionKey(ConsoleKeyInfo key, out bool shouldRender)
    {
        shouldRender = false;

        if (!HasOnlyControlModifier(key) ||
            key.Key is not (ConsoleKey.F1 or ConsoleKey.F2))
        {
            return false;
        }

        return TryHandleFunctionKey(key, out shouldRender);
    }

    private bool CanExecuteFunctionKeyCommand(string commandId) =>
        _commandRegistry.CanExecute(commandId, _commandContext);

    private bool ExecuteRegisteredCommand(string commandId, object? args = null) =>
        _commandRegistry.Execute(commandId, _commandContext, args).ShouldRender;

    private bool TryHandleFarCommandLineShortcut(ConsoleKeyInfo key)
    {
        if (IsPlainControlKey(key, ConsoleKey.E, '\u0005'))
            return BrowseCommandHistory(-1, CommandHistoryNavigationStart.Newest);

        if (IsPlainControlKey(key, ConsoleKey.X, '\u0018'))
            return BrowseCommandHistory(+1, CommandHistoryNavigationStart.Newest);

        if (IsPlainControlKey(key, ConsoleKey.F, '\u0006'))
            return InsertCurrentItemFullPathIntoCommandLine();

        if (IsPlainControlEnter(key))
            return InsertCurrentItemNameIntoCommandLine();

        if (IsPlainControlOpenBracket(key))
            return InsertPanelCurrentDirectoryIntoCommandLine(_left);

        if (IsPlainControlCloseBracket(key))
            return InsertPanelCurrentDirectoryIntoCommandLine(_right);

        return false;
    }

    private bool InsertCurrentItemNameIntoCommandLine()
    {
        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null)
            return true;

        InsertTextIntoCommandLine(item.Name);
        return true;
    }

    private bool InsertCurrentItemFullPathIntoCommandLine()
    {
        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null)
            return true;

        InsertTextIntoCommandLine(item.FullPath);
        return true;
    }

    private bool InsertPanelCurrentDirectoryIntoCommandLine(FilePanelState state)
    {
        // Ensure the inserted directory path ends with a directory separator.
        string path = state.CurrentDirectory;
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            path += Path.DirectorySeparatorChar;
        InsertTextIntoCommandLine(path);
        return true;
    }

    private void InsertTextIntoCommandLine(string text)
    {
        _cmdLine.InsertText(QuoteCommandLineInsertion(text));

        if (HasVisiblePanels)
            OnVisibleCommandLineTextEdited();
        else
            ResetCommandHistoryNavigation();
    }

    private bool HandleHiddenCommandLineKey(ConsoleKeyInfo key)
    {
        if (TryHandlePanelVisibilityFunctionKey(key, out bool shouldRender))
            return shouldRender;

        if (IsPlainControlKey(key, ConsoleKey.A, '\u0001'))
        {
            _cmdLine.SelectAll();
            return true;
        }

        if (IsPlainControlKey(key, ConsoleKey.C, '\u0003'))
            return CopyCommandLineSelection();

        if (IsPlainControlKey(key, ConsoleKey.V, '\u0016'))
            return PasteTextIntoCommandLine();

        if (TryHandleCommandLineNavigationKey(key, forceCommandLine: true))
            return true;

        switch (key.Key)
        {
            case ConsoleKey.Delete:
                ResetCommandHistoryNavigation();
                _cmdLine.DeleteForward();
                return true;

            case ConsoleKey.Backspace:
                ResetCommandHistoryNavigation();
                _cmdLine.DeleteBack();
                return true;

            case ConsoleKey.Escape:
                ResetCommandHistoryNavigation();
                _cmdLine.Clear();
                return true;

            case ConsoleKey.Enter:
                ResetCommandHistoryNavigation();
                if (_cmdLine.HasText)
                    ExecuteCommand(_cmdLine.Text);
                return true;

            case ConsoleKey.UpArrow:
                return BrowseCommandHistory(-1, CommandHistoryNavigationStart.Newest);

            case ConsoleKey.DownArrow:
                return BrowseCommandHistory(+1, CommandHistoryNavigationStart.Oldest);

            case ConsoleKey.F10:
                _state.Running = false;
                return false;
        }

        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (isPrintable)
        {
            ResetCommandHistoryNavigation();
            _cmdLine.Insert(key.KeyChar);
            return true;
        }

        return false;
    }

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
        _screen.SetConsoleScrollbackEnabled(true);
        _screen.SetRenderingOutputMode(false);
        _shellUnderlay.RestoreOrClear();

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

    private readonly record struct PanelItemClick(
        PanelSide PanelSide,
        int ItemIndex,
        string FullPath);
}
