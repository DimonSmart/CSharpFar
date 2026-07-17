using CSharpFar.App.AutoRefresh;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.App.Dialogs;
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
        bool enableBuiltInNetworkModules = true,
        string? configDirectory = null,
        ITextClipboard? clipboard = null,
        ITerminalScreenMode? terminalScreenMode = null,
        IFileMetadataService? fileMetadata = null,
        Func<IFileAttributesDialog>? fileAttributesDialogFactory = null)
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
        var effectiveFileMetadata = fileMetadata ?? new FileMetadataService();
        var menuProvider = core.MenuProvider;
        var callbacks = new ApplicationServiceCallbacks
        {
            // Services can render a modal before the Application facade binds its
            // command callbacks (for example, focused command tests).
            PanelOptions = () => effectiveSettings.Panels.Options,
            CanExecuteFunctionKeyCommand = _ => false,
        };
        var keyboardInputContext = new KeyboardInputContext
        {
            PanelController = controller,
            CommandLine = session.CommandLine.State,
            SetActiveSide = side => callbacks.SetActiveSide(side),
            LeftPanel = () => session.Panels.Left,
            RightPanel = () => session.Panels.Right,
            PanelOptions = () => callbacks.PanelOptions(),
            QuickView = () => session.App.QuickView,
            SetQuickView = quickView => session.App.QuickView = quickView,
            SetRunning = running => session.App.Running = running,
            SetFunctionKeyLayer = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ExecuteRegisteredCommand = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            SelectAllCommandLineTextOrPanelItems = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            CopyCommandLineSelection = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            PasteTextIntoCommandLine = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            OnVisibleCommandLineTextEdited = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            CloseSearchResultsPanel = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ExecuteCommand = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            BrowseCommandHistory = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            HideCommandCompletion = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ResetCommandHistoryNavigation = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            TryGoUp = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            OpenCurrentItem = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            OpenPanelItem = (_, _, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            CanExecuteFunctionKeyCommand = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
        };
        var shortcutTextProvider = new CommandShortcutTextProvider(
            new DefaultKeyboardShortcutBindingProvider().GetBindings(),
            functionKeyBindingProvider.GetBindings());
        var menuLayoutService = new MenuLayoutService(shortcutTextProvider);
        var highlightService = FileHighlightServiceFactory.Create(effectiveSettings);
        var commandNavigation = CommandServicesFactory.CreateNavigation(effectiveHistory, session);
        var commandCompletionController = commandNavigation.CommandCompletionController;
        var commandHistoryNavigator = commandNavigation.CommandHistoryNavigator;
        var pendingMenuCommands = new PendingMenuCommandQueue();
        var menuController = new TopMenuController(
            session.Menu.State,
            pendingMenuCommands.Enqueue);
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
        var panelQuickSearch = new PanelQuickSearchController(
            controller,
            () => callbacks.GetActiveSide(),
            () => callbacks.IsPanelsMode(),
            side => callbacks.GetPanelState(side),
            side => callbacks.VisibleRowsForSide(side));
        var panelWorkspace = new PanelWorkspaceController(
            screen,
            session,
            panelQuickSearch,
            () => callbacks.PanelOptions());
        var keyboardInputRouter = new KeyboardInputRouter(keyboardInputContext);
        var mouseInputContext = new MouseInputContext
        {
            PanelController = controller,
            CommandLine = session.CommandLine.State,
            Ui = session.Ui,
            Mouse = session.Mouse,
            PanelOptions = () => callbacks.PanelOptions(),
            SetActiveSide = side => callbacks.SetActiveSide(side),
            GetPanelState = side => callbacks.GetPanelState(side),
        };
        var commandLineInputHandler = new ApplicationCommandLineInputHandler(mouseInputContext);
        var panelInputHandler = new ApplicationPanelInputHandler(mouseInputContext);
        var panelScrollbarInputHandler = new ApplicationPanelScrollbarInputHandler(mouseInputContext);
        var functionKeyBarInputHandler = new ApplicationFunctionKeyBarInputHandler(mouseInputContext);
        var directoryShortcutBarInputHandler = new ApplicationDirectoryShortcutBarInputHandler(mouseInputContext);
        var applicationInputDispatcher = new ApplicationInputDispatcher(
            keyboardInputRouter,
            commandLineInputHandler,
            panelInputHandler,
            panelScrollbarInputHandler,
            functionKeyBarInputHandler,
            directoryShortcutBarInputHandler);
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
        var composition = rendering.Composition;
        var applicationSurface = rendering.ApplicationSurface;
        var modalDialogs = rendering.ModalDialogs;
        var commandCompletionLayer = new CommandCompletionLayer(
            rendering.RenderContext,
            commandCompletionController,
            temporarily => commandCompletionController.Hide(temporarily),
            commandHistoryNavigator.Reset);
        var panelQuickSearchLayer = new PanelQuickSearchLayer(
            rendering.RenderContext,
            temporarily => commandCompletionController.Hide(temporarily),
            commandHistoryNavigator.Reset);
        var topMenuLayer = new TopMenuLayer(
            rendering.RenderContext,
            menuController,
            menuLayoutService);
        var applicationUiLayers = new ApplicationUiLayerScope(
            composition,
            commandCompletionLayer,
            panelQuickSearchLayer,
            topMenuLayer);
        var quickViewDirectorySize = rendering.QuickViewDirectorySize;
        var searchResults = new PanelSearchResultsService(
            screen,
            modalDialogs,
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
        var panelFileViewer = new PanelFileViewerService(
            screen,
            modalDialogs,
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
            modalDialogs,
            () => session.App.Palette,
            (state, item) => callbacks.ViewPanelFile(state, item),
            (workDir, displayCommand, execute) => callbacks.ExecuteInCurrentConsole(workDir, displayCommand, execute));
        var moduleUiServices = new ModuleUiServices
        {
            Screen = screen,
            ModalDialogs = modalDialogs,
            Palette = () => session.App.Palette,
        };
        var moduleCatalog = ModuleCatalogFactory.Create(
            enableBuiltInNetworkModules ? sftpModule ?? new SftpModule() : null,
            enableBuiltInNetworkModules ? ftpModule ?? new FtpModule() : null,
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
            modalDialogs,
            () => session.App.Palette,
            () => callbacks.PanelOptions(),
            side => callbacks.GetPanelState(side),
            side => callbacks.SetActiveSide(side),
            quickView => callbacks.SetQuickView(quickView));
        var workspaceModeController = new ApplicationWorkspaceModeController(
            screen,
            session,
            panelQuickSearch,
            commandCompletionController,
            commandHistoryNavigator,
            menuController.Close,
            terminalSurface,
            composition);
        var commandServices = CommandServicesFactory.Create(
            screen,
            composition,
            modalDialogs,
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
            workspaceModeController,
            panelFileViewer,
            panelFileOpener,
            moduleCatalog,
            modulePanelOpener,
            terminalSurface,
            commandLineRenderer,
            commandCompletionController,
            commandHistoryNavigator,
            menuController,
            saveSettings,
            volumeService,
            effectiveFileMetadata,
            fileAttributesDialogFactory ?? (() => new FileAttributesDialog(
                modalDialogs,
                canOpenSystemProperties: System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))),
            highlightService);
        var runtime = ApplicationRuntimeBuilder.Create(
            composition,
            applicationSurface,
            applicationUiLayers,
            pendingMenuCommands,
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
            WorkspaceModeController = workspaceModeController,
            PanelFileViewer = panelFileViewer,
            PanelFileOpener = panelFileOpener,
            ModuleCatalog = moduleCatalog,
            ModulePanelOpener = modulePanelOpener,
            CommandRegistry = commandServices.CommandRegistry,
            RenderContext = rendering.RenderContext,
            RenderCoordinator = renderCoordinator,
            CommandCompletionLayer = commandCompletionLayer,
            PanelQuickSearchLayer = panelQuickSearchLayer,
            TopMenuLayer = topMenuLayer,
            ApplicationSurface = applicationSurface,
            Composition = composition,
            ModalDialogs = modalDialogs,
            TerminalSurface = terminalSurface,
            Runtime = runtime,
            KeyboardInputContext = keyboardInputContext,
            KeyboardInputRouter = keyboardInputRouter,
            ApplicationInputDispatcher = applicationInputDispatcher,
            MouseInputContext = mouseInputContext,
            SaveSettings = saveSettings,
        };
    }
}
