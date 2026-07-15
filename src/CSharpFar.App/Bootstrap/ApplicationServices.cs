using CSharpFar.App.AutoRefresh;
using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.App.Files;
using CSharpFar.App.FunctionKeys;
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
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.History;
using CSharpFar.Core.Services;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal sealed class ApplicationServices
{
    public required ScreenRenderer Screen { get; init; }
    public required PanelController PanelController { get; init; }
    public required CommandHistoryNavigator CommandHistoryNavigator { get; init; }
    public required CommandCompletionController CommandCompletionController { get; init; }
    public required CommandLineCommandExecutor CommandLineCommandExecutor { get; init; }
    public required ExternalConsoleCommandRunner ExternalConsoleCommandRunner { get; init; }
    public required ApplicationCommandContext CommandContext { get; init; }
    public required AppSettingsAlias Settings { get; init; }
    public required ITextClipboard Clipboard { get; init; }
    public required ApplicationSession Session { get; init; }
    public required DefaultMenuDefinitionProvider MenuProvider { get; init; }
    public required ApplicationServiceCallbacks Callbacks { get; init; }
    public required TopMenuController MenuController { get; init; }
    public required PanelAutoRefreshService AutoRefresh { get; init; }
    public required PanelSortServiceFacade PanelSort { get; init; }
    public required PanelNavigationService PanelNavigation { get; init; }
    public required PanelSearchResultsService SearchResults { get; init; }
    public required PanelRefreshService PanelRefresh { get; init; }
    public required PanelQuickSearchController PanelQuickSearch { get; init; }
    public required PanelWorkspaceController PanelWorkspace { get; init; }
    public required PanelVisibilityController PanelVisibility { get; init; }
    public required PanelFileViewerService PanelFileViewer { get; init; }
    public required PanelFileOpener PanelFileOpener { get; init; }
    public required NativeModuleCatalog ModuleCatalog { get; init; }
    public required ModulePanelOpener ModulePanelOpener { get; init; }
    public required ApplicationCommandRegistry CommandRegistry { get; init; }
    public required ApplicationRenderContext RenderContext { get; init; }
    public required ApplicationRenderCoordinator RenderCoordinator { get; init; }
    public required ApplicationUiLayerScope ApplicationUiLayers { get; init; }
    public required CommandCompletionLayer CommandCompletionLayer { get; init; }
    public required PanelQuickSearchLayer PanelQuickSearchLayer { get; init; }
    public required TopMenuLayer TopMenuLayer { get; init; }
    public required ApplicationUiSurface ApplicationSurface { get; init; }
    public required UiCompositionHost Composition { get; init; }
    public required ModalDialogHost ModalDialogs { get; init; }
    public required TerminalSurfaceController TerminalSurface { get; init; }
    public required ApplicationRuntime Runtime { get; init; }
    public required KeyboardInputContext KeyboardInputContext { get; init; }
    public required KeyboardInputRouter KeyboardInputRouter { get; init; }
    public required MouseInputContext MouseInputContext { get; init; }
    public required MouseInputRouter MouseInputRouter { get; init; }
    public Action? SaveSettings { get; init; }
}
