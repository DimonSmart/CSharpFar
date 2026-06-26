using CSharpFar.Console.Models;
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

    public void RenderUntilStable()
    {
        while (_context.App.Running)
        {
            Render();
            if (!_context.Screen.FrameWasInterrupted)
            {
                _context.Screen.DrainResizeEvents();
                break;
            }
        }
    }

    public void RenderCommandLineOnlyUntilStable(bool restoreHiddenScreenBeforeEachAttempt = false)
    {
        while (_context.App.Running)
        {
            if (restoreHiddenScreenBeforeEachAttempt)
                _context.TerminalSurface.RestoreHiddenScreen();

            RenderCommandLineOnly();
            if (!_context.Screen.FrameWasInterrupted)
            {
                _context.Screen.DrainResizeEvents();
                break;
            }
        }
    }

    public void Render()
    {
        UpdateQuickViewDirSize();
        _context.TerminalSurface.ApplyMode();
        if (!_context.TerminalSurface.UsesTerminalScreenMode && _context.HasHiddenPanels())
            _context.TerminalSurface.RestoreHiddenScreen();

        _context.Screen.SetRenderingOutputMode(true);
        using var frame = _context.Screen.BeginFrame();
        _context.Screen.SetCursorVisible(false);

        var viewport = _context.Screen.FrameViewport;
        var size = viewport.Size;
        _context.Ui.LastRenderViewport = viewport;
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
        _context.Ui.LeftBounds = panelBounds.Left;
        _context.Ui.RightBounds = panelBounds.Right;

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
                        _context.Ui.LeftBounds,
                        _context.Ui.RightBounds,
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

    public void RenderCommandLineOnly()
    {
        _context.TerminalSurface.ApplyMode();
        _context.Screen.SetRenderingOutputMode(true);

        var viewport = _context.Screen.GetViewport();
        var size = viewport.Size;
        int row = ApplicationLayoutService.CommandLineRow(size);
        _context.TerminalSurface.PrepareHiddenCommandLineOverlay(viewport, row, size.Width);

        using var frame = _context.Screen.BeginFrame();

        viewport = _context.Screen.FrameViewport;
        size = viewport.Size;
        _context.Ui.LastRenderViewport = viewport;

        row = ApplicationLayoutService.CommandLineRow(size);
        _commandLineRenderer.Render(
            row,
            size,
            _context.ActiveState().CurrentDirectory,
            _context.CommandLine);
        _commandLineRenderer.PositionCursor(
            row,
            size,
            _context.ActiveState().CurrentDirectory,
            _context.CommandLine);
    }

    private void UpdateQuickViewDirSize()
    {
        var item = _context.ActiveSide() == PanelSide.Left
            ? _context.PanelController.CurrentItem(_context.LeftPanel())
            : _context.PanelController.CurrentItem(_context.RightPanel());
        _context.QuickViewDirectorySize.Update(_context.App.QuickView, item);
    }
}
