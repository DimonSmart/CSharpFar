using CSharpFar.App.AutoRefresh;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.App.Files;
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
using CSharpFar.Core.Models;
using CSharpFar.Shell;
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
        IShellService shell,
        IFileOperationService fileOperations,
        IFileLauncher fileLauncher,
        ISearchService searchService,
        IHistoryStore history,
        UserMenuStore userMenu,
        ITextClipboard clipboard,
        AppSettingsAlias settings,
        ApplicationSession session,
        DefaultMenuDefinitionProvider menuProvider,
        ApplicationServiceCallbacks callbacks,
        PanelController panelController,
        PanelAutoRefreshService autoRefresh,
        PanelRefreshService panelRefresh,
        PanelSortServiceFacade panelSort,
        PanelNavigationService panelNavigation,
        PanelSearchResultsService searchResults,
        PanelQuickSearchController panelQuickSearch,
        PanelWorkspaceController panelWorkspace,
        PanelVisibilityController panelVisibility,
        PanelFileViewerService panelFileViewer,
        PanelFileOpener panelFileOpener,
        NativeModuleCatalog moduleCatalog,
        ModulePanelOpener modulePanelOpener,
        FarNetPanelActionService farNetPanelActions,
        TerminalSurfaceController terminalSurface,
        ApplicationCommandLineRenderer commandLineRenderer,
        CommandCompletionController commandCompletionController,
        CommandHistoryNavigator commandHistoryNavigator,
        TopMenuController menuController,
        Action? saveSettings,
        IVolumeService? volumeService,
        IFileHighlightService? highlightService)
    {
        var changeDirectoryCommandExecutor = new ChangeDirectoryCommandExecutor(
            panelController,
            () => callbacks.ActiveState(),
            () => callbacks.GetActiveSide(),
            () => callbacks.PanelOptions(),
            (state, side) => callbacks.StartWatching(state, side));
        var externalConsoleCommandRunner = new ExternalConsoleCommandRunner(
            screen,
            terminalSurface,
            commandLineRenderer,
            session.App,
            session.CommandLine.State,
            () => callbacks.RefreshPanels());
        var commandLineCommandExecutor = new CommandLineCommandExecutor(
            session.CommandLine.State,
            commandHistoryNavigator,
            history,
            modulePanelOpener,
            changeDirectoryCommandExecutor,
            shell,
            externalConsoleCommandRunner,
            () => callbacks.ActiveState(),
            () => callbacks.GetActiveSide(),
            panelQuickSearch.Close,
            temporarily => commandCompletionController.Hide(temporarily));
        var commandRegistry = ApplicationCommandRegistry.CreateDefault();
        var commandContext = new ApplicationCommandContext(
            screen,
            panelController,
            fileLauncher,
            fileOperations,
            searchService,
            history,
            userMenu,
            clipboard,
            settings,
            session,
            menuProvider,
            panelWorkspace,
            autoRefresh,
            panelRefresh,
            panelSort,
            panelNavigation,
            searchResults,
            panelQuickSearch,
            panelVisibility,
            panelFileViewer,
            panelFileOpener,
            moduleCatalog,
            modulePanelOpener,
            farNetPanelActions,
            commandLineCommandExecutor,
            externalConsoleCommandRunner,
            commandCompletionController,
            commandHistoryNavigator,
            menuController,
            saveSettings,
            volumeService,
            highlightService);

        return new CommandServices(
            commandRegistry,
            changeDirectoryCommandExecutor,
            externalConsoleCommandRunner,
            commandLineCommandExecutor,
            commandContext);
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
    ApplicationCommandContext CommandContext);
