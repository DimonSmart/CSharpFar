using CSharpFar.App.AutoRefresh;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.App.Files;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Highlighting;
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
using CSharpFar.Core.History;
using CSharpFar.Core.Services;
using CSharpFar.FarNetHost;
using CSharpFar.FileSystem;
using CSharpFar.Module.Abstractions;
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
        ITextClipboard? clipboard = null,
        ITerminalScreenMode? terminalScreenMode = null)
    {
        var core = CoreServicesFactory.Create(
            fs,
            history,
            settings,
            userMenu,
            volumeInfoService,
            mountPointService,
            fileLauncher,
            searchService,
            sourceRegistry,
            configDirectory,
            clipboard);
        var effectiveSettings = core.Settings;
        var effectiveSourceRegistry = core.SourceRegistry;
        var controller = core.PanelController;
        var effectiveHistory = core.History;
        var functionKeyBindingProvider = core.FunctionKeyBindingProvider;
        var session = core.Session;
        var effectiveConfigDirectory = core.ConfigDirectory;
        var effectiveSearchService = core.SearchService;
        var effectiveFileLauncher = core.FileLauncher;
        var effectiveClipboard = core.Clipboard;
        var effectiveUserMenu = core.UserMenu;
        var menuProvider = core.MenuProvider;
        var callbacks = new ApplicationServiceCallbacks();
        var keyboardInputContext = new KeyboardInputContext
        {
            MenuState = session.Menu.State,
            MenuController = null!,
            PanelQuickSearch = null!,
            PanelController = controller,
            CommandLine = session.CommandLine.State,
            FunctionKeyBindings = functionKeyBindingProvider.GetBindings(),
            BuildMenuDefinition = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ActiveSide = () => callbacks.GetActiveSide(),
            SetActiveSide = side => callbacks.SetActiveSide(side),
            ActiveState = () => callbacks.ActiveState(),
            LeftPanel = () => session.Panels.Left,
            RightPanel = () => session.Panels.Right,
            HasVisiblePanels = () => callbacks.HasVisiblePanels(),
            IsPanelVisible = side => callbacks.IsPanelVisible(side),
            PanelOptions = () => callbacks.PanelOptions(),
            VisibleRows = () => callbacks.VisibleRows(),
            VisibleRowsForSide = side => callbacks.VisibleRowsForSide(side),
            QuickView = () => session.App.QuickView,
            SetQuickView = quickView => session.App.QuickView = quickView,
            SetRunning = running => session.App.Running = running,
            TogglePanels = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            TryHandleFarNetPanelShortcut = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ExecuteRegisteredCommand = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            SelectAllCommandLineTextOrPanelItems = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            CopyCommandLineSelection = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            PasteTextIntoCommandLine = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            MovePanelColumn = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            OnVisibleCommandLineTextEdited = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            TryHideCommandCompletionTemporarily = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            CloseSearchResultsPanel = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            TryAcceptCommandCompletion = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ExecuteCommand = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            EnsureActivePanelVisible = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            TryMoveCommandCompletionSelection = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            BrowseCommandHistory = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            HideCommandCompletion = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ResetCommandHistoryNavigation = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            TryGoUp = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            CanExecuteFunctionKeyCommand = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
        };
        var menuLayoutService = new MenuLayoutService();
        var highlightService = FileHighlightServiceFactory.Create(effectiveSettings);
        var commandNavigation = CommandServicesFactory.CreateNavigation(effectiveHistory, session);
        var commandCompletionController = commandNavigation.CommandCompletionController;
        var commandHistoryNavigator = commandNavigation.CommandHistoryNavigator;
        var menuController = new TopMenuController(
            session.Menu.State,
            request => callbacks.ExecuteMenuCommand(request));
        var autoRefresh = new PanelAutoRefreshService(
            changeWatcher,
            locationService,
            () => callbacks.PanelOptions(),
            side => callbacks.GetPanelState(side),
            side => callbacks.VisibleRowsForSide(side),
            (state, rows) => callbacks.SafeRefresh(state, rows));
        var panelSort = new PanelSortServiceFacade(
            controller,
            () => callbacks.PanelOptions(),
            state => callbacks.ClosePanelQuickSearchForState(state));
        var panelNavigation = new PanelNavigationService(
            controller,
            effectiveHistory,
            () => callbacks.PanelOptions(),
            side => callbacks.VisibleRowsForSide(side),
            side => callbacks.ClosePanelQuickSearchForPanel(side),
            (state, side) => callbacks.StartWatching(state, side));
        var searchResults = new PanelSearchResultsService(
            screen,
            effectiveSearchService,
            () => session.App.Palette,
            controller,
            effectiveHistory,
            () => callbacks.PanelOptions(),
            state => callbacks.PanelSideForState(state),
            side => callbacks.VisibleRowsForSide(side),
            state => callbacks.ClosePanelQuickSearchForState(state),
            side => callbacks.ClosePanelQuickSearchForPanel(side),
            (state, side) => callbacks.StartWatching(state, side),
            panelSort.SortVirtualPanel);
        var panelRefresh = new PanelRefreshService(
            controller,
            () => callbacks.PanelOptions(),
            side => callbacks.VisibleRowsForSide(side),
            state => callbacks.ClosePanelQuickSearchForState(state),
            searchResults.RefreshPanel);
        var panelQuickSearch = new PanelQuickSearchController(
            controller,
            () => callbacks.GetActiveSide(),
            () => callbacks.HasVisiblePanels(),
            side => callbacks.IsPanelVisible(side),
            side => callbacks.GetPanelState(side),
            side => callbacks.VisibleRowsForSide(side));
        var panelWorkspace = new PanelWorkspaceController(
            screen,
            session,
            panelQuickSearch,
            () => callbacks.PanelOptions());
        keyboardInputContext.MenuController = menuController;
        keyboardInputContext.PanelQuickSearch = panelQuickSearch;
        var keyboardInputRouter = new KeyboardInputRouter(keyboardInputContext);
        var mouseInputContext = new MouseInputContext
        {
            MenuState = session.Menu.State,
            MenuController = menuController,
            MenuLayoutService = menuLayoutService,
            PanelQuickSearch = panelQuickSearch,
            PanelController = controller,
            CommandLine = session.CommandLine.State,
            CommandCompletion = session.CommandLine.Completion,
            CommandCompletionController = commandCompletionController,
            Ui = session.Ui,
            Mouse = session.Mouse,
            FunctionKeyBindings = functionKeyBindingProvider.GetBindings(),
            FunctionKeyLayer = () => session.FunctionKeyLayer,
            DirectoryShortcuts = () => effectiveSettings.DirectoryShortcuts,
            PanelOptions = () => callbacks.PanelOptions(),
            CurrentScreenSize = screen.GetSize,
            LastRenderSizeOrCurrent = () => session.Ui.LastRenderViewport?.Size ?? screen.GetSize(),
            ActiveSide = () => callbacks.GetActiveSide(),
            SetActiveSide = side => callbacks.SetActiveSide(side),
            ActiveState = () => callbacks.ActiveState(),
            GetPanelState = side => callbacks.GetPanelState(side),
            ViewModeForSide = side => side == CSharpFar.Core.Models.PanelSide.Left
                ? session.Panels.LeftViewMode
                : session.Panels.RightViewMode,
            IsPanelVisible = side => callbacks.IsPanelVisible(side),
            HasVisiblePanels = () => callbacks.HasVisiblePanels(),
            QuickView = () => session.App.QuickView,
            VisibleRowsForSide = side => callbacks.VisibleRowsForSide(side),
        };
        var mouseInputRouter = new MouseInputRouter(mouseInputContext);
        var panelFileViewer = new PanelFileViewerService(
            screen,
            () => session.App.Palette,
            effectiveSourceRegistry,
            effectiveHistory,
            effectiveClipboard,
            effectiveSettings,
            controller,
            state => callbacks.PanelSideForState(state),
            side => callbacks.VisibleRowsForSide(side),
            (state, rows) => callbacks.SafeRefresh(state, rows));
        var panelFileOpener = new PanelFileOpener(
            effectiveFileLauncher,
            screen,
            () => session.App.Palette,
            (state, item) => callbacks.ViewPanelFile(state, item),
            (workDir, displayCommand, execute) => callbacks.ExecuteInCurrentConsole(workDir, displayCommand, execute));
        var moduleUiServices = new ModuleUiServices
        {
            Screen = screen,
            Palette = () => session.App.Palette,
        };
        farNetModuleHost?.Initialize(new FarNetModuleHostServices
        {
            Ui = moduleUiServices,
            DataRoot = Path.Combine(effectiveConfigDirectory, "FarNet"),
            GetActivePanelSide = () => callbacks.GetActiveSide(),
            GetPanelState = side => callbacks.GetPanelState(side),
        });
        var moduleCatalog = ModuleCatalogFactory.Create(
            enableBuiltInNetworkModules ? sftpModule ?? new SftpModule() : null,
            enableBuiltInNetworkModules ? ftpModule ?? new FtpModule() : null,
            farNetModuleHost,
            new ModuleStartupInfo
            {
                Ui = moduleUiServices,
                Settings = new ModuleSettingsService(effectiveConfigDirectory),
                Credentials = credentialStore,
                Panels = new ApplicationModulePanelHost(callbacks),
            });
        var modulePanelOpener = new ModulePanelOpener(
            moduleCatalog,
            effectiveSourceRegistry,
            controller,
            screen,
            () => session.App.Palette,
            () => callbacks.PanelOptions(),
            side => callbacks.GetPanelState(side),
            side => callbacks.SetActiveSide(side),
            quickView => callbacks.SetQuickView(quickView));
        var farNetPanelActions = new FarNetPanelActionService(
            effectiveSourceRegistry,
            controller,
            screen,
            () => session.App.Palette,
            effectiveSettings,
            effectiveClipboard,
            modulePanelOpener,
            side => callbacks.VisibleRowsForSide(side),
            state => callbacks.PanelSideForState(state),
            (state, rows) => callbacks.SafeRefresh(state, rows));
        var rendering = RenderingServicesFactory.Create(
            screen,
            terminalScreenMode,
            session,
            controller,
            panelQuickSearch,
            panelWorkspace,
            autoRefresh,
            functionKeyBindingProvider,
            menuLayoutService,
            callbacks,
            effectiveSettings,
            highlightService);
        var terminalSurface = rendering.TerminalSurface;
        var commandLineRenderer = rendering.CommandLineRenderer;
        var renderCoordinator = rendering.RenderCoordinator;
        var quickViewDirectorySize = rendering.QuickViewDirectorySize;
        var panelVisibility = new PanelVisibilityController(
            screen,
            session,
            panelWorkspace,
            panelQuickSearch,
            commandCompletionController,
            commandHistoryNavigator,
            terminalSurface,
            renderCoordinator);
        var commandServices = CommandServicesFactory.Create(
            screen,
            shell,
            fileOps,
            effectiveFileLauncher,
            effectiveSearchService,
            effectiveHistory,
            effectiveUserMenu,
            effectiveClipboard,
            effectiveSettings,
            session,
            menuProvider,
            callbacks,
            controller,
            autoRefresh,
            panelRefresh,
            panelSort,
            panelNavigation,
            searchResults,
            panelQuickSearch,
            panelWorkspace,
            panelVisibility,
            panelFileViewer,
            panelFileOpener,
            moduleCatalog,
            modulePanelOpener,
            farNetPanelActions,
            terminalSurface,
            commandLineRenderer,
            commandCompletionController,
            commandHistoryNavigator,
            menuController,
            saveSettings,
            volumeService,
            highlightService);
        var runtime = ApplicationRuntimeBuilder.Create(
            screen,
            callbacks,
            autoRefresh,
            quickViewDirectorySize);

        return new ApplicationServices
        {
            Screen = screen,
            PanelController = controller,
            CommandHistoryNavigator = commandHistoryNavigator,
            CommandCompletionController = commandCompletionController,
            CommandLineCommandExecutor = commandServices.CommandLineCommandExecutor,
            ExternalConsoleCommandRunner = commandServices.ExternalConsoleCommandRunner,
            CommandContext = commandServices.CommandContext,
            Settings = effectiveSettings,
            Clipboard = effectiveClipboard,
            Session = session,
            MenuProvider = menuProvider,
            Callbacks = callbacks,
            MenuController = menuController,
            AutoRefresh = autoRefresh,
            PanelSort = panelSort,
            PanelNavigation = panelNavigation,
            SearchResults = searchResults,
            PanelRefresh = panelRefresh,
            PanelQuickSearch = panelQuickSearch,
            PanelWorkspace = panelWorkspace,
            PanelVisibility = panelVisibility,
            PanelFileViewer = panelFileViewer,
            PanelFileOpener = panelFileOpener,
            ModuleCatalog = moduleCatalog,
            ModulePanelOpener = modulePanelOpener,
            FarNetPanelActions = farNetPanelActions,
            CommandRegistry = commandServices.CommandRegistry,
            RenderContext = rendering.RenderContext,
            RenderCoordinator = renderCoordinator,
            TerminalSurface = terminalSurface,
            Runtime = runtime,
            KeyboardInputContext = keyboardInputContext,
            KeyboardInputRouter = keyboardInputRouter,
            MouseInputContext = mouseInputContext,
            MouseInputRouter = mouseInputRouter,
            SaveSettings = saveSettings,
        };
    }
}
