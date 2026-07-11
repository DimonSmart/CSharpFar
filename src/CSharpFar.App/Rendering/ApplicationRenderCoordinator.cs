using CSharpFar.Console.Models;
using CSharpFar.Ui;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationRenderCoordinator
{
    private readonly ApplicationRenderContext _context;
    private readonly ApplicationPanelWorkspaceRenderer _panelWorkspaceRenderer;
    private readonly ClockRenderer _clockRenderer;
    private readonly ApplicationFunctionKeyBarRenderer _functionKeyBarRenderer;
    private readonly ApplicationOverlayRenderer _overlayRenderer;
    private readonly ApplicationCommandLineRenderer _commandLineRenderer;

    public ApplicationRenderCoordinator(
        ApplicationRenderContext context,
        ApplicationPanelWorkspaceRenderer panelWorkspaceRenderer,
        ClockRenderer clockRenderer,
        ApplicationFunctionKeyBarRenderer functionKeyBarRenderer,
        ApplicationOverlayRenderer overlayRenderer,
        ApplicationCommandLineRenderer commandLineRenderer)
    {
        _context = context;
        _panelWorkspaceRenderer = panelWorkspaceRenderer;
        _clockRenderer = clockRenderer;
        _functionKeyBarRenderer = functionKeyBarRenderer;
        _overlayRenderer = overlayRenderer;
        _commandLineRenderer = commandLineRenderer;
    }

    public void RenderMainContent(UiRenderContext context)
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
        context.PublishOnStable(
            new ApplicationLayoutSnapshot(context.Viewport, panelBounds.Left, panelBounds.Right),
            snapshot =>
            {
                _context.Ui.LastRenderViewport = snapshot.Viewport;
                _context.Ui.LeftBounds = snapshot.LeftBounds;
                _context.Ui.RightBounds = snapshot.RightBounds;
            });

        if (_context.HasVisiblePanels())
            new DirectoryShortcutBarRenderer(_context.Screen, _context.App.Palette)
                .Render(panelHeight - 1, size.Width, _context.DirectoryShortcuts());

        if (_context.IsPanelVisible(PanelSide.Right))
            _clockRenderer.Render(size);

        _commandLineRenderer.Render(
            panelHeight,
            size,
            _context.ActiveState().CurrentDirectory,
            _context.CommandLine);
        _overlayRenderer.RenderCommandCompletion(size, panelHeight, _context.CommandCompletion);

        _functionKeyBarRenderer.Render(size, _context.FunctionKeyLayer());

        _overlayRenderer.RenderMenuOverlay(size, _context.BuildMenuDefinition(), _context.MenuState);

        if (_context.MenuState.OpenState == MenuOpenState.Closed)
        {
            if (_context.PanelQuickSearch.State is not null)
            {
                if (!_overlayRenderer.RenderPanelQuickSearch(
                        _context.PanelQuickSearch.State,
                        panelBounds.Left,
                        panelBounds.Right,
                        _context.IsPanelVisible))
                {
                    _context.Screen.SetCursorVisible(false);
                }
            }
            else
            {
                _commandLineRenderer.PositionCursor(
                    panelHeight,
                    size,
                    _context.ActiveState().CurrentDirectory,
                    _context.CommandLine);
            }
        }
        else
            _context.Screen.SetCursorVisible(false);
    }

    public void RenderHiddenCommandLineContent(UiRenderContext context)
    {
        var viewport = context.Viewport;
        var size = context.Size;
        int row = ApplicationLayoutService.CommandLineRow(size);
        context.PublishOnStable(viewport, value => _context.Ui.LastRenderViewport = value);
        _commandLineRenderer.Render(row, size, _context.ActiveState().CurrentDirectory, _context.CommandLine);
        _commandLineRenderer.PositionCursor(row, size, _context.ActiveState().CurrentDirectory, _context.CommandLine);
    }

    private void UpdateQuickViewDirSize()
    {
        var item = _context.ActiveSide() == PanelSide.Left
            ? _context.PanelController.CurrentItem(_context.LeftPanel())
            : _context.PanelController.CurrentItem(_context.RightPanel());
        _context.QuickViewDirectorySize.Update(_context.App.QuickView, item);
    }

    private readonly record struct ApplicationLayoutSnapshot(ConsoleViewport Viewport, Rect LeftBounds, Rect RightBounds);
}
