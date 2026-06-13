using CSharpFar.App.AutoRefresh;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Menu;
using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Highlighting;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal static class RenderingServicesFactory
{
    public static RenderingServices Create(
        ScreenRenderer screen,
        ITerminalScreenMode? terminalScreenMode,
        ApplicationSession session,
        PanelController controller,
        PanelQuickSearchController panelQuickSearch,
        PanelWorkspaceController panelWorkspace,
        PanelAutoRefreshService autoRefresh,
        DefaultFunctionKeyBindingProvider functionKeyBindingProvider,
        MenuLayoutService menuLayoutService,
        ApplicationServiceCallbacks callbacks,
        AppSettingsAlias settings,
        IFileHighlightService? highlightService)
    {
        var panelWorkspaceRenderer = new ApplicationPanelWorkspaceRenderer(
            screen,
            () => session.App.Palette,
            controller,
            () => highlightService,
            () => callbacks.PanelOptions());
        var clockRenderer = new ClockRenderer(screen, () => session.App.Palette);
        var functionKeyBarRenderer = new ApplicationFunctionKeyBarRenderer(
            screen,
            functionKeyBindingProvider.GetBindings(),
            commandId => callbacks.CanExecuteFunctionKeyCommand(commandId));
        var overlayRenderer = new ApplicationOverlayRenderer(
            screen,
            () => session.App.Palette,
            menuLayoutService);
        var commandLineRenderer = new ApplicationCommandLineRenderer(
            screen,
            () => session.App.Palette);
        var shellUnderlay = new ShellUnderlayService(screen);
        var terminalSurface = new TerminalSurfaceController(
            screen,
            terminalScreenMode,
            shellUnderlay,
            session.Ui,
            () => callbacks.HasVisiblePanels());
        var quickViewDirectorySize = new QuickViewDirectorySizeController(autoRefresh.WakeInputLoop);
        var renderContext = new ApplicationRenderContext
        {
            Screen = screen,
            TerminalSurface = terminalSurface,
            PanelController = controller,
            App = session.App,
            Ui = session.Ui,
            MenuState = session.Menu.State,
            PanelQuickSearch = panelQuickSearch,
            CommandLine = session.CommandLine.State,
            CommandCompletion = session.CommandLine.Completion,
            LeftPanel = session.Panels.Left,
            RightPanel = session.Panels.Right,
            ActiveSide = () => panelWorkspace.ActiveSide,
            ActiveState = () => panelWorkspace.ActiveState,
            LeftViewMode = () => session.Panels.LeftViewMode,
            RightViewMode = () => session.Panels.RightViewMode,
            FunctionKeyLayer = () => session.FunctionKeyLayer,
            HasHiddenPanels = () => panelWorkspace.HasHiddenPanels,
            HasVisiblePanels = () => panelWorkspace.HasVisiblePanels,
            IsPanelVisible = panelWorkspace.IsPanelVisible,
            DirectoryShortcuts = () => settings.DirectoryShortcuts,
            QuickViewDirectorySize = quickViewDirectorySize,
        };
        var renderCoordinator = new ApplicationRenderCoordinator(
            renderContext,
            panelWorkspaceRenderer,
            clockRenderer,
            functionKeyBarRenderer,
            overlayRenderer,
            commandLineRenderer);

        return new RenderingServices(
            commandLineRenderer,
            terminalSurface,
            quickViewDirectorySize,
            renderContext,
            renderCoordinator);
    }
}

internal sealed record RenderingServices(
    ApplicationCommandLineRenderer CommandLineRenderer,
    TerminalSurfaceController TerminalSurface,
    QuickViewDirectorySizeController QuickViewDirectorySize,
    ApplicationRenderContext RenderContext,
    ApplicationRenderCoordinator RenderCoordinator);
