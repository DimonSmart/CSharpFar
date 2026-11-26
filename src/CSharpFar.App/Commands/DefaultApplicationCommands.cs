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
        PanelDirectorySizeCoordinator? directorySizes = null) =>
    [
        new HelpCommand(interactiveSurfaces, session.App),
        new AboutCommand(dialogs, uriLauncher, updateCheckService, applicationUpdateInstaller, session.App),
        new DiagnosticsCommand(dialogs, clipboard, diagnosticLog, terminalSurface),
        new UserMenuCommand(),
        new UserMenuCommand(globalOnly: true),
        new ViewFileCommand(directorySizes),
        new CalculateDirectorySizesCommand(directorySizes),
        new EditFileCommand(),
        new OpenCreateFileCommand(),
        new CopyCommand(targetResolver, panelController, dialogs, formFields, sourceRegistry, fileOperationWorkflow, showHelp),
        new CopyCommand(targetResolver, panelController, dialogs, formFields, sourceRegistry, fileOperationWorkflow, showHelp, toCurrentDirectory: true),
        new MoveCommand(targetResolver, panelController, dialogs, formFields, sourceRegistry, fileOperationWorkflow),
        new RenameCommand(targetResolver, panelController, dialogs, formFields, sourceRegistry, fileOperationWorkflow),
        new CreateFolderCommand(targetResolver, panelController, dialogs, sourceRegistry, fileOperationWorkflow, panelRefresh),
        new DeleteCommand(targetResolver, panelController, dialogs, sourceRegistry, fileOperationWorkflow, settings),
        new OpenFileAttributesCommand(targetResolver, panelController, dialogs, fileMetadata, fileAttributesDialogFactory, panelRefresh, session.Panels),
        new OpenTopMenuCommand(topMenu, targetResolver),
        new QuitCommand(settings, dialogs, session.App),
        new SelectLeftDriveCommand(),
        new SelectRightDriveCommand(),
        new SearchFilesCommand(),
        new CompareCommand(CompareCommandKind.Folders),
        new CompareCommand(CompareCommandKind.FileSets),
        new CommandHistoryCommand(),
        new FileHistoryCommand(),
        new DirectoryHistoryCommand(),
        new TogglePanelsCommand(workspaceModeController),
        new SwapPanelsCommand(),
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
        new PrintTerminalDiagnosticsCommand(),
        new OpenSettingsCommand(),
        new OpenDirectoryShortcutEditorCommand(),
        new OpenPluginsCommand(),
        new OpenUserMenuEditorCommand(),
        new OpenUserMenuEditorCommand(local: true),
        new NavigateToDirectoryShortcutCommand(),
        new ToggleShowHiddenAndSystemFilesCommand(),
        new ToggleHighlightFilesCommand(),
        new ToggleSelectFoldersCommand(),
        new ToggleRightClickSelectsFilesCommand(),
        new ToggleSortFoldersByExtensionCommand(),
        new ToggleShowStatusLineCommand(),
        new ToggleShowFilesTotalInformationCommand(),
        new ToggleShowFreeSizeCommand(),
        new ToggleShowSortModeLetterCommand(),
        new ToggleShowParentDirectoryInRootFoldersCommand(),
        new SaveSettingsCommand(saveSettings),
    ];
}
