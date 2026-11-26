using CSharpFar.App.UserMenu;
using CSharpFar.App.Plugins;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Highlighting;
using CSharpFar.App.Files;
using CSharpFar.App.Viewer;
using CSharpFar.Core.Models;
using CSharpFar.App.AutoRefresh;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Services;
using CSharpFar.App.Panels;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Diagnostics;
using CSharpFar.App.Menu;
using CSharpFar.App.Modules;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.App.Updates;
using CSharpFar.Core.Abstractions;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Commands;

internal static class DefaultApplicationCommands
{
    public static IReadOnlyList<IApplicationCommand> Create(
        InteractiveSurfaceHost interactiveSurfaces,
        ApplicationSession session,
        DialogService dialogs,
        IUriLauncher uriLauncher,
        UpdateCheckService updateCheckService,
        IApplicationUpdateInstaller applicationUpdateInstaller,
        ITextClipboard clipboard,
        IDiagnosticLog diagnosticLog,
        TerminalSurfaceController terminalSurface,
        AppSettingsAlias settings,
        Action? saveSettings,
        ApplicationWorkspaceModeController workspaceModeController,
        ModulePanelOpener modulePanelOpener,
        PanelWorkspaceController panelWorkspace,
        PanelRefreshService panelRefresh,
        PanelSortServiceFacade panelSort,
        TopMenu topMenu,
        PanelCommandTargetResolver targetResolver,
        PanelController panelController,
        PanelNavigationService panelNavigation,
        PanelAutoRefreshService autoRefresh,
        FilePanelSourceRegistry sourceRegistry,
        PanelSearchResultsService searchResults,
        PanelFileOpener panelFileOpener,
        FormFieldFactory formFields,
        PanelFileOperationWorkflow fileOperationWorkflow,
        Action<HelpTopic> showHelp,
        IFileMetadataService fileMetadata,
        Func<IFileAttributesDialog> fileAttributesDialogFactory,
        PanelSettingsChangeService settingsChange,
        FileHighlightState highlightState,
        PanelFileViewerService panelFileViewer,
        IHistoryStore history,
        ISearchService searchService,
        CommandCompletionController commandCompletionController,
        CommandHistoryNavigator commandHistoryNavigator,
        Action resetFunctionKeyLayer,
        UserMenuStore userMenu,
        CommandLineCommandExecutor commandLineCommandExecutor,
        ExternalConsoleCommandRunner externalConsoleCommandRunner,
        IVolumeService? volumeService,
        NativeModuleCatalog moduleCatalog,
        ModalDialogHost modalDialogs,
        Action resetTransientNavigationUi,
        ExternalPluginSession externalPlugins,
        PanelDirectorySizeCoordinator? directorySizes = null) =>
    [
        new HelpCommand(interactiveSurfaces, session.App),
        new AboutCommand(dialogs, uriLauncher, updateCheckService, applicationUpdateInstaller, session.App),
        new DiagnosticsCommand(dialogs, clipboard, diagnosticLog, terminalSurface),
        new UserMenuCommand(targetResolver, panelController, panelWorkspace, sourceRegistry, userMenu, dialogs, formFields, commandLineCommandExecutor),
        new UserMenuCommand(targetResolver, panelController, panelWorkspace, sourceRegistry, userMenu, dialogs, formFields, commandLineCommandExecutor, globalOnly: true),
        new ViewFileCommand(targetResolver, panelController, panelFileViewer, directorySizes),
        new CalculateDirectorySizesCommand(targetResolver, directorySizes),
        new EditFileCommand(targetResolver, panelController, panelFileViewer, panelRefresh, dialogs),
        new OpenCreateFileCommand(targetResolver, panelController, panelFileViewer, panelRefresh, panelWorkspace, dialogs, formFields, settings, history, sourceRegistry),
        new CopyCommand(targetResolver, panelController, dialogs, formFields, sourceRegistry, fileOperationWorkflow, showHelp),
        new CopyCommand(targetResolver, panelController, dialogs, formFields, sourceRegistry, fileOperationWorkflow, showHelp, toCurrentDirectory: true),
        new MoveCommand(targetResolver, panelController, dialogs, formFields, sourceRegistry, fileOperationWorkflow),
        new RenameCommand(targetResolver, panelController, dialogs, formFields, sourceRegistry, fileOperationWorkflow),
        new CreateFolderCommand(targetResolver, panelController, dialogs, sourceRegistry, fileOperationWorkflow, panelRefresh),
        new DeleteCommand(targetResolver, panelController, dialogs, sourceRegistry, fileOperationWorkflow, settings),
        new OpenFileAttributesCommand(targetResolver, panelController, dialogs, fileMetadata, fileAttributesDialogFactory, panelRefresh, session.Panels),
        new OpenTopMenuCommand(topMenu, targetResolver),
        new QuitCommand(settings, dialogs, session.App),
        new SelectLeftDriveCommand(panelWorkspace, session.Panels, session.App, volumeService, moduleCatalog, modulePanelOpener, modalDialogs, dialogs, panelNavigation, autoRefresh, history, resetFunctionKeyLayer),
        new SelectRightDriveCommand(panelWorkspace, session.Panels, session.App, volumeService, moduleCatalog, modulePanelOpener, modalDialogs, dialogs, panelNavigation, autoRefresh, history, resetFunctionKeyLayer),
        new SearchFilesCommand(targetResolver, sourceRegistry, dialogs, formFields, searchService, searchResults, resetFunctionKeyLayer),
        new CompareCommand(panelWorkspace, session.Panels, sourceRegistry, dialogs, formFields, settings, panelController, saveSettings, resetFunctionKeyLayer, CompareCommandKind.Folders),
        new CompareCommand(panelWorkspace, session.Panels, sourceRegistry, dialogs, formFields, settings, panelController, saveSettings, resetFunctionKeyLayer, CompareCommandKind.FileSets),
        new CommandHistoryCommand(dialogs, history, session.CommandLine.State, commandCompletionController, commandHistoryNavigator, resetFunctionKeyLayer),
        new FileHistoryCommand(targetResolver, panelController, panelFileViewer, panelRefresh, dialogs, history, sourceRegistry, resetFunctionKeyLayer),
        new DirectoryHistoryCommand(targetResolver, dialogs, history, sourceRegistry, panelNavigation, autoRefresh, panelWorkspace, resetFunctionKeyLayer),
        new TogglePanelsCommand(workspaceModeController),
        new SwapPanelsCommand(panelWorkspace, session.Panels, autoRefresh, resetTransientNavigationUi),
        new SortByNameCommand(targetResolver, panelSort),
        new SortByExtensionCommand(targetResolver, panelSort),
        new SortByLastWriteTimeCommand(targetResolver, panelSort),
        new SortBySizeCommand(targetResolver, panelSort),
        new OpenCurrentItemCommand(panelController, panelWorkspace, searchResults, panelNavigation, modulePanelOpener, panelFileOpener),
        new NavigateToRootCommand(panelWorkspace, panelNavigation, autoRefresh, sourceRegistry),
        new PanelSetViewModeCommand(session.Panels, settings, panelController, panelWorkspace, saveSettings),
        new PanelSetSortModeCommand(panelWorkspace, panelSort),
        new PanelToggleReverseSortCommand(panelWorkspace, panelRefresh, panelSort),
        new PanelRefreshCommand(panelWorkspace, panelRefresh),
        new ModuleOpenCommand(modulePanelOpener, panelWorkspace),
        new PrintTerminalDiagnosticsCommand(terminalSurface, panelWorkspace, session.Panels, externalConsoleCommandRunner),
        new OpenSettingsCommand(settings, session.Panels, session.App, dialogs, highlightState, settingsChange),
        new OpenDirectoryShortcutEditorCommand(panelWorkspace, settings, dialogs, formFields, saveSettings),
        new OpenPluginsCommand(dialogs, externalPlugins),
        new OpenUserMenuEditorCommand(targetResolver, sourceRegistry, userMenu, dialogs, formFields),
        new OpenUserMenuEditorCommand(targetResolver, sourceRegistry, userMenu, dialogs, formFields, local: true),
        new NavigateToDirectoryShortcutCommand(panelWorkspace, sourceRegistry, settings, dialogs, panelNavigation, autoRefresh, resetTransientNavigationUi),
        new ToggleShowHiddenAndSystemFilesCommand(settings, settingsChange),
        new ToggleHighlightFilesCommand(settings, settingsChange, highlightState),
        new ToggleSelectFoldersCommand(settings, settingsChange),
        new ToggleRightClickSelectsFilesCommand(settings, settingsChange),
        new ToggleSortFoldersByExtensionCommand(settings, settingsChange),
        new ToggleShowStatusLineCommand(settings, settingsChange),
        new ToggleShowFilesTotalInformationCommand(settings, settingsChange),
        new ToggleShowFreeSizeCommand(settings, settingsChange),
        new ToggleShowSortModeLetterCommand(settings, settingsChange),
        new ToggleShowParentDirectoryInRootFoldersCommand(settings, settingsChange),
        new SaveSettingsCommand(saveSettings),
    ];
}
