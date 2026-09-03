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
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Module.Abstractions;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.ProcessesAndPorts;
using CSharpFar.Module.Sftp;
using CSharpFar.Platform.Abstractions;
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
        Func<IFileAttributesDialog>? fileAttributesDialogFactory = null,
        ApplicationRunOptions? runOptions = null,
        IProcessesAndPortsPlatformService? processesAndPorts = null,
        IFileUsagePlatformService? fileUsage = null)
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
            clipboard,
            runOptions);
        var effectiveSettings = core.Settings;
        var effectiveSourceRegistry = core.SourceRegistry;
        var controller = core.PanelController;
        var effectiveHistory = core.History;
        var functionKeyBindingProvider = core.FunctionKeyBindingProvider;
        var session = core.Session;
        var effectiveConfigDirectory = core.ConfigDirectory;
        var fieldHistoryStore = new History.JsonSingleLineTextHistoryStore(effectiveConfigDirectory);
        var fieldHistoryRegistry = new SingleLineTextHistoryRegistry(fieldHistoryStore);
        var formFields = new FormFieldFactory(fieldHistoryRegistry);
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
            SetQuickView = quickView =>
            {
                if (quickView) session.App.FileUsage = false;
                session.App.QuickView = quickView;
            },
            SetRunning = running => session.App.Running = running,
            SetFunctionKeyLayer = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ExecuteRegisteredCommand = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ToggleSelectAllPanelItems = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            CopyCommandLineSelection = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            PasteTextIntoCommandLine = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            OnCommandLineTextEdited = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            CloseSearchResultsPanel = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ExecuteCommand = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            BrowseCommandHistory = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            HideCommandCompletion = _ => throw new InvalidOperationException("Keyboard input context is not assigned."),
            ResetCommandHistoryNavigation = () => throw new InvalidOperationException("Keyboard input context is not assigned."),
            TryGoUp = (_, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
            OpenPanelItem = (_, _, _) => throw new InvalidOperationException("Keyboard input context is not assigned."),
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
        var autoRefresh = new PanelAutoRefreshService(
            changeWatcher,
            controller,
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
            directoryShortcutBarInputHandler,
            new ApplicationQuickViewInputHandler(mouseInputContext));
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
            highlightService,
            fileUsage ?? new UnsupportedFileUsagePlatformService());
        var terminalSurface = rendering.TerminalSurface;
        var commandLineRenderer = rendering.CommandLineRenderer;
        var renderCoordinator = rendering.RenderCoordinator;
        var composition = rendering.Composition;
        var applicationSurface = rendering.ApplicationSurface;
        var modalDialogs = rendering.ModalDialogs;
        var interactiveSurfaces = new InteractiveSurfaceHost(composition);
        var commandCompletionLayer = new CommandCompletionLayer(
            rendering.RenderContext,
            commandCompletionController,
            temporarily => commandCompletionController.Hide(temporarily),
            commandHistoryNavigator.Reset);
        var panelQuickSearchLayer = new PanelQuickSearchLayer(
            rendering.RenderContext,
            temporarily => commandCompletionController.Hide(temporarily),
            commandHistoryNavigator.Reset);
        var topMenu = new TopMenu(
            () => session.App.WorkspaceMode == ApplicationWorkspaceMode.Panels,
            () => rendering.RenderContext.BuildMenuDefinition(),
            () => MenuRenderOptionsFactory.Create(session.App.Palette),
            () => session.Panels.ActiveSide == PanelSide.Left ? "Left" : "Right",
            current => current switch
            {
                "Left" => "Right",
                "Right" => "Left",
                _ => session.Panels.ActiveSide == PanelSide.Left ? "Right" : "Left",
            },
            panelQuickSearch.Close,
            pendingMenuCommands.Enqueue,
            menuLayoutService);
        var applicationUiLayers = new ApplicationUiLayerScope(
            composition,
            commandCompletionLayer,
            panelQuickSearchLayer,
            topMenu);
        var quickViewDirectorySize = rendering.QuickViewDirectorySize;
        var fileUsagePanel = rendering.FileUsagePanel;
        keyboardInputContext.ToggleFileUsage = () =>
        {
            if (session.App.FileUsage)
            {
                session.App.FileUsage = false;
                session.App.QuickView = session.App.RestoreQuickViewAfterFileUsage;
                fileUsagePanel.Update(false, PanelSourceId.Local, null);
            }
            else
            {
                session.App.RestoreQuickViewAfterFileUsage = session.App.QuickView;
                session.App.QuickView = false;
                session.App.FileUsage = true;
            }
            return true;
        };
        keyboardInputContext.MoveFileUsageOwnerSelection = offset =>
            session.App.FileUsage && fileUsagePanel.MoveSelection(offset);
        keyboardInputContext.UnlockFileUsageOwner = () =>
            session.App.FileUsage && fileUsagePanel.RequestUnlock(message =>
                new MessageDialog(modalDialogs).ShowButtons("Unlock owner", message, ["Unlock", "Cancel"]) == 0);
        mouseInputContext.SelectFileUsageOwner = index =>
            session.App.FileUsage && fileUsagePanel.SelectOwner(index);
        keyboardInputContext.ToggleQuickViewDirectoryMonitor = () =>
        {
            if (!session.App.QuickView)
                return false;
            quickViewDirectorySize.ToggleMonitor();
            return true;
        };
        keyboardInputContext.ActivateQuickViewDirectoryMonitorChange = () =>
        {
            if (!session.App.QuickView || !quickViewDirectorySize.TryGetSelectedMonitorTarget(out string target))
                return false;

            PanelSide targetSide = panelWorkspace.ActiveSide;
            FilePanelState targetPanel = targetSide == PanelSide.Left ? session.Panels.Left : session.Panels.Right;
            if (targetPanel.SourceId != PanelSourceId.Local || targetPanel.ContentKind != PanelContentKind.Source)
                return false;

            string? parent = Path.GetDirectoryName(target);
            if (parent is null || !controller.TryLoadDirectory(targetPanel, parent, callbacks.PanelOptions()))
                return false;
            controller.SetCursorByName(targetPanel, Path.GetFileName(target), callbacks.VisibleRowsForSide(targetSide));
            callbacks.StartWatching(targetPanel, targetSide);
            callbacks.SetQuickView(false);
            return true;
        };
        mouseInputContext.ActivateQuickViewDirectoryMonitorChange = changeId =>
        {
            if (!session.App.QuickView || !quickViewDirectorySize.SelectMonitorChange(changeId))
                return false;
            return keyboardInputContext.ActivateQuickViewDirectoryMonitorChange();
        };
        mouseInputContext.ToggleQuickViewDirectoryMonitor = keyboardInputContext.ToggleQuickViewDirectoryMonitor;
        mouseInputContext.HandleQuickViewRecentChangesInput = confirmed =>
        {
            if (!session.App.QuickView) return false;
            quickViewDirectorySize.SynchronizeRecentChangesSelection();
            return !confirmed || keyboardInputContext.ActivateQuickViewDirectoryMonitorChange();
        };
        keyboardInputContext.MoveQuickViewDirectoryMonitorSelection = offset =>
            session.App.QuickView && quickViewDirectorySize.MoveMonitorSelection(offset);
        keyboardInputContext.MoveQuickViewDirectoryMonitorSelectionByPage = direction =>
            session.App.QuickView && quickViewDirectorySize.MoveMonitorSelectionByPage(direction);
        var dialogs = new DialogService(modalDialogs, formFields);
        var searchResults = new PanelSearchResultsService(
            screen,
            modalDialogs,
            dialogs,
            effectiveSearchService,
            () => session.App.Palette,
            controller,
            effectiveHistory,
            () => callbacks.PanelOptions(),
            state => callbacks.PanelSideForState(state),
            side => callbacks.VisibleRowsForSide(side),
            state => callbacks.ClosePanelQuickSearchForState(state),
            side => callbacks.ClosePanelQuickSearchForPanel(side),
            (state, side) => callbacks.StartWatching(state, side));
        var panelRefresh = new PanelRefreshService(
            controller,
            () => callbacks.PanelOptions(),
            side => callbacks.VisibleRowsForSide(side),
            state => callbacks.ClosePanelQuickSearchForState(state),
            searchResults.RefreshPanel);
        panelRefresh.RefreshRequested = state =>
        {
            if (session.App.FileUsage && ReferenceEquals(state, panelWorkspace.ActiveState))
                fileUsagePanel.Refresh();
        };
        var panelFileViewer = new PanelFileViewerService(
            interactiveSurfaces,
            modalDialogs,
            dialogs,
            () => session.App.Palette,
            effectiveSourceRegistry,
            effectiveHistory,
            effectiveClipboard,
            formFields,
            effectiveSettings,
            controller,
            state => callbacks.PanelSideForState(state),
            side => callbacks.VisibleRowsForSide(side),
            (state, rows) => callbacks.SafeRefresh(state, rows));
        var panelFileOpener = new PanelFileOpener(
            effectiveFileLauncher,
            dialogs,
            () => session.App.Palette,
            (state, item) => callbacks.ViewPanelFile(state, item),
            (workDir, displayCommand, execute) => callbacks.ExecuteInCurrentConsole(workDir, displayCommand, execute));
        var moduleUiServices = new ModuleUiServices
        {
            Screen = screen,
            ModalDialogs = modalDialogs,
            Palette = () => session.App.Palette,
            Fields = formFields,
            Dialogs = dialogs,
        };
        var moduleCatalog = ModuleCatalogFactory.Create(
            enableBuiltInNetworkModules ? sftpModule ?? new SftpModule() : null,
            enableBuiltInNetworkModules ? ftpModule ?? new FtpModule() : null,
            new ProcessesAndPortsModule(processesAndPorts ?? new UnsupportedProcessesAndPortsPlatformService()),
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
            dialogs,
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
            topMenu.Close,
            terminalSurface,
            composition);
        var commandServices = CommandServicesFactory.Create(
            screen,
            interactiveSurfaces,
            modalDialogs,
            dialogs,
            shell,
            fileOps,
            effectiveFileLauncher,
            effectiveSearchService,
            effectiveHistory,
            formFields,
            effectiveUserMenu,
            effectiveClipboard,
            effectiveSourceRegistry,
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
            topMenu,
            saveSettings,
            volumeService,
            effectiveFileMetadata,
            fileAttributesDialogFactory ?? (() => new FileAttributesDialog(
                dialogs,
                formFields,
                canOpenSystemProperties: System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))),
            highlightService);
        var runtime = ApplicationRuntimeBuilder.Create(
            composition,
            screen,
            applicationSurface,
            applicationUiLayers,
            pendingMenuCommands,
            session,
            callbacks,
            autoRefresh,
            quickViewDirectorySize,
            fileUsagePanel);

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
            AutoRefresh = autoRefresh,
            PanelSort = panelSort,
            PanelNavigation = panelNavigation,
            SearchResults = searchResults,
            PanelRefresh = panelRefresh,
            PanelQuickSearch = panelQuickSearch,
            FileUsagePanel = fileUsagePanel,
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
            TopMenu = topMenu,
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
