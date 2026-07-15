using CSharpFar.Console.Models;
using CSharpFar.Ui;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationRenderCoordinator
{
    private readonly ApplicationRenderContext _context;
    private readonly ApplicationPanelWorkspaceRenderer _panelWorkspaceRenderer;
    private readonly ClockRenderer _clockRenderer;
    private readonly ApplicationFunctionKeyBarRenderer _functionKeyBarRenderer;
    private readonly ApplicationCommandLineRenderer _commandLineRenderer;

    public ApplicationRenderCoordinator(
        ApplicationRenderContext context,
        ApplicationPanelWorkspaceRenderer panelWorkspaceRenderer,
        ClockRenderer clockRenderer,
        ApplicationFunctionKeyBarRenderer functionKeyBarRenderer,
        ApplicationCommandLineRenderer commandLineRenderer)
    {
        _context = context;
        _panelWorkspaceRenderer = panelWorkspaceRenderer;
        _clockRenderer = clockRenderer;
        _functionKeyBarRenderer = functionKeyBarRenderer;
        _commandLineRenderer = commandLineRenderer;
    }

    public ApplicationUiFrame RenderMainContent(UiRenderContext context)
    {
        UpdateQuickViewDirSize();
        _context.Screen.SetCursorVisible(false);
        var size = context.Size;
        var panelBounds = _panelWorkspaceRenderer.Render(
            size,
            _context.LeftPanel(),
            _context.RightPanel(),
            _context.ActiveSide(),
            _context.LeftViewMode(),
            _context.RightViewMode(),
            _context.App.QuickView,
            _context.QuickViewDirectorySize.CurrentState,
            _context.IsPanelVisible);
        int panelHeight = panelBounds.PanelHeight;
        context.PublishOnStable(context.Viewport, value => _context.Ui.LastRenderViewport = value);

        if (_context.HasVisiblePanels())
            new DirectoryShortcutBarRenderer(_context.Screen, _context.App.Palette)
                .Render(panelHeight - 1, size.Width, _context.DirectoryShortcuts());

        if (_context.IsPanelVisible(PanelSide.Right))
            _clockRenderer.Render(size);

        ApplicationCommandLineFrame commandLine = _commandLineRenderer.Render(
            panelHeight,
            size,
            _context.ActiveState().CurrentDirectory,
            _context.CommandLine);

        _functionKeyBarRenderer.Render(size, _context.FunctionKeyLayer());

        return new ApplicationUiFrame(
            context.Viewport,
            ApplicationSurfaceMode.Panels,
            commandLine,
            panelBounds.Left,
            panelBounds.Right);
    }

    public ApplicationUiFrame RenderHiddenCommandLineContent(UiRenderContext context)
    {
        var viewport = context.Viewport;
        var size = context.Size;
        int row = ApplicationLayoutService.CommandLineRow(size);
        context.PublishOnStable(viewport, value => _context.Ui.LastRenderViewport = value);
        ApplicationCommandLineFrame commandLine = _commandLineRenderer.Render(
            row,
            size,
            _context.ActiveState().CurrentDirectory,
            _context.CommandLine);

        return new ApplicationUiFrame(
            context.Viewport,
            ApplicationSurfaceMode.HiddenCommandLine,
            commandLine,
            null,
            null);
    }

    private void UpdateQuickViewDirSize()
    {
        var item = _context.ActiveSide() == PanelSide.Left
            ? _context.PanelController.CurrentItem(_context.LeftPanel())
            : _context.PanelController.CurrentItem(_context.RightPanel());
        _context.QuickViewDirectorySize.Update(_context.App.QuickView, item);
    }
}
