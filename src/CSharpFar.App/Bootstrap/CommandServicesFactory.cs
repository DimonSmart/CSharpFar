using CSharpFar.App.Highlighting;
using CSharpFar.App.Plugins;
using CSharpFar.Core.Menu;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Diagnostics;
using CSharpFar.App.Files;
using CSharpFar.App.Menu;
using CSharpFar.App.Modules;
using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.App.UserMenu;
using CSharpFar.App.Updates;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Shell;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal static class CommandServicesFactory
{
    public static CommandNavigationServices CreateNavigation(IHistoryStore history, ApplicationSession session) =>
        new(
            new CommandCompletionController(history, session.CommandLine.Completion),
            new CommandHistoryNavigator(history));

    public static CommandServices Create(
        ScreenRenderer screen,
        InteractiveSurfaceHost interactiveSurfaces,
        ModalDialogHost modalDialogs,
        DialogService dialogs,
        IShellService shell,
        IFileOperationService fileOperations,
        IUriLauncher uriLauncher,
        ISearchService searchService,
        IHistoryStore history,
        FormFieldFactory formFields,
        UserMenuStore userMenu,
        ITextClipboard clipboard,
        FilePanelSourceRegistry sourceRegistry,
        AppSettingsAlias settings,
        ApplicationSession session,
        DefaultMenuDefinitionProvider menuProvider,
        PanelController panelController,
        PanelAutoRefreshService autoRefresh,
        PanelDirectorySizeCoordinator directorySizes,
        PanelRefreshService panelRefresh,
        PanelSortServiceFacade panelSort,
        PanelNavigationService panelNavigation,
        PanelSearchResultsService searchResults,
        PanelQuickSearchController panelQuickSearch,
        PanelWorkspaceController panelWorkspace,
        ApplicationWorkspaceModeController workspaceModeController,
        PanelFileViewerService panelFileViewer,
        PanelFileOpener panelFileOpener,
        NativeModuleCatalog moduleCatalog,
        ModulePanelOpener modulePanelOpener,
        TerminalSurfaceController terminalSurface,
        ApplicationCommandLineRenderer commandLineRenderer,
        CommandCompletionController commandCompletionController,
        CommandHistoryNavigator commandHistoryNavigator,
        TopMenu topMenu,
        Action? saveSettings,
        IVolumeService? volumeService,
        IFileMetadataService fileMetadata,
        Func<IFileAttributesDialog> fileAttributesDialogFactory,
        FileHighlightState highlightState,
        IDiagnosticLog diagnosticLog,
        UpdateCheckService updateCheckService,
        IApplicationUpdateInstaller applicationUpdateInstaller)
    {
        var changeDirectoryCommandExecutor = new ChangeDirectoryCommandExecutor(
            panelController,
            () => panelWorkspace.ActiveState,
            () => panelWorkspace.ActiveSide,
            () => sourceRegistry.TryGetSource(PanelSourceId.Local, out _),
            () => settings.Panels.Options,
            (state, side) => autoRefresh.StartWatching(state, side),
            navigation: panelNavigation);
        var externalConsoleCommandRunner = new ExternalConsoleCommandRunner(
            screen,
            terminalSurface,
            commandLineRenderer,
            session.App,
            session.CommandLine.State,
            () => panelRefresh.RefreshPanels(session.Panels.Left, session.Panels.Right));
        var commandLineCommandExecutor = new CommandLineCommandExecutor(
            session.CommandLine.State,
            commandHistoryNavigator,
            history,
            modulePanelOpener,
            changeDirectoryCommandExecutor,
            new EnvironmentVariableCommandExecutor(),
            new ShellInvocationParser(ShellComposition.CreateRegistry()),
            shell,
            externalConsoleCommandRunner,
            () => panelWorkspace.ActiveState,
            () => panelWorkspace.ActiveSide,
            panelQuickSearch.Close,
            temporarily => commandCompletionController.Hide(temporarily));
        var targetResolver = new PanelCommandTargetResolver(panelWorkspace);
        var operationRunner = new FileOperationUiRunner(modalDialogs, dialogs, () => session.App.Palette,
            fileOperations, () => settings.FileOperations.ShowTotalProgress, formFields);
        var fileOperationWorkflow = new PanelFileOperationWorkflow(operationRunner, fileOperations, dialogs,
            settings, panelRefresh, session.Panels);
        var settingsChange = new PanelSettingsChangeService(panelRefresh, session.Panels, saveSettings);
        var externalPlugins = new ExternalPluginSession();
        var commandRegistry = new ApplicationCommandRegistry(DefaultApplicationCommands.Create(
            interactiveSurfaces, session, dialogs, uriLauncher, updateCheckService, applicationUpdateInstaller,
            clipboard, diagnosticLog, terminalSurface, settings, saveSettings, workspaceModeController,
            modulePanelOpener, panelWorkspace, panelRefresh, panelSort, topMenu, targetResolver,
            panelController, panelNavigation, autoRefresh, sourceRegistry, searchResults, panelFileOpener,
            formFields, fileOperationWorkflow, topic => new HelpViewer(interactiveSurfaces, session.App.Palette).Show(topic),
            fileMetadata, fileAttributesDialogFactory, settingsChange, highlightState, panelFileViewer,
            history, searchService, commandCompletionController, commandHistoryNavigator,
            () => session.FunctionKeyLayer = CSharpFar.App.FunctionKeys.FunctionKeyLayer.Plain,
            userMenu, commandLineCommandExecutor, externalConsoleCommandRunner, volumeService, moduleCatalog, modalDialogs,
            () => { panelQuickSearch.Close(); commandCompletionController.Hide(temporarily: false); commandHistoryNavigator.Reset(); },
            externalPlugins, directorySizes));
        MenuBarDefinition BuildMenuDefinition() =>
            menuProvider.BuildMenu(new MenuBuildContext
            {
                ActivePanelSide = panelWorkspace.ActiveSide,
                LeftPanel = session.Panels.Left,
                RightPanel = session.Panels.Right,
                LeftViewMode = session.Panels.LeftViewMode,
                RightViewMode = session.Panels.RightViewMode,
                Settings = settings,
                CanSaveSettings = saveSettings is not null,
                CanEditLocalUserMenu = PanelCommandUserMenuOperands.CanUseLocalUserMenu(
                    sourceRegistry, targetResolver.ResolvePanelTarget(new PanelCommandArgs { PanelSide = panelWorkspace.ActiveSide })),
                ModuleMenuItems = moduleCatalog.MenuItems,
            });

        return new CommandServices(
            commandRegistry,
            changeDirectoryCommandExecutor,
            externalConsoleCommandRunner,
            commandLineCommandExecutor,
            targetResolver, externalPlugins, BuildMenuDefinition);
    }
}

internal sealed record CommandNavigationServices(
    CommandCompletionController CommandCompletionController,
    CommandHistoryNavigator CommandHistoryNavigator);

internal sealed record CommandServices(
    ApplicationCommandRegistry CommandRegistry,
    ChangeDirectoryCommandExecutor ChangeDirectoryCommandExecutor,
    ExternalConsoleCommandRunner ExternalConsoleCommandRunner,
    CommandLineCommandExecutor CommandLineCommandExecutor,
    PanelCommandTargetResolver TargetResolver,
    ExternalPluginSession ExternalPlugins,
    Func<MenuBarDefinition> BuildMenuDefinition);
