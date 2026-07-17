using CSharpFar.App.State;
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
        PanelSide activeSide = _context.ActiveSide();
        FilePanelState activeState = activeSide == PanelSide.Left
            ? _context.LeftPanel()
            : _context.RightPanel();
        ApplicationPanelKeyboardFrame leftKeyboard = PanelRenderer.BuildKeyboardFrame(_context.LeftPanel());
        ApplicationPanelKeyboardFrame rightKeyboard = PanelRenderer.BuildKeyboardFrame(_context.RightPanel());
        var workspace = _panelWorkspaceRenderer.Render(
            size,
            _context.LeftPanel(),
            _context.RightPanel(),
            activeSide,
            _context.LeftViewMode(),
            _context.RightViewMode(),
            _context.App.QuickView,
            _context.QuickViewDirectorySize.CurrentState);
        int panelHeight = workspace.PanelHeight;
        context.PublishOnStable(context.Viewport, value => _context.Ui.LastRenderViewport = value);

        ApplicationDirectoryShortcutBarFrame? directoryShortcutBar =
            new DirectoryShortcutBarRenderer(_context.Screen, _context.App.Palette)
                .Render(panelHeight - 1, size.Width, _context.DirectoryShortcuts());

        _clockRenderer.Render(size);

        ApplicationCommandLineFrame commandLine = _commandLineRenderer.Render(
            panelHeight,
            size,
            activeState.CurrentDirectory,
            _context.CommandLine);

        ApplicationFunctionKeyBarFrame? functionKeyBar =
            _functionKeyBarRenderer.Render(size, _context.FunctionKeyLayer());

        return new ApplicationUiFrame(
            context.Viewport,
            ApplicationWorkspaceMode.Panels,
            BuildKeyboardFrame(activeSide, leftKeyboard, rightKeyboard),
            commandLine,
            WithKeyboard(workspace.LeftPanel, leftKeyboard),
            WithKeyboard(workspace.RightPanel, rightKeyboard),
            functionKeyBar,
            directoryShortcutBar);
    }

    public ApplicationUiFrame RenderHiddenCommandLineContent(UiRenderContext context)
    {
        var viewport = context.Viewport;
        var size = context.Size;
        int row = ApplicationLayoutService.CommandLineRow(size);
        PanelSide activeSide = _context.ActiveSide();
        FilePanelState activeState = activeSide == PanelSide.Left
            ? _context.LeftPanel()
            : _context.RightPanel();
        ApplicationPanelKeyboardFrame leftKeyboard = PanelRenderer.BuildKeyboardFrame(_context.LeftPanel());
        ApplicationPanelKeyboardFrame rightKeyboard = PanelRenderer.BuildKeyboardFrame(_context.RightPanel());
        context.PublishOnStable(viewport, value => _context.Ui.LastRenderViewport = value);
        ApplicationCommandLineFrame commandLine = _commandLineRenderer.Render(
            row,
            size,
            activeState.CurrentDirectory,
            _context.CommandLine);

        return new ApplicationUiFrame(
            context.Viewport,
            ApplicationWorkspaceMode.HiddenCommandLine,
            BuildKeyboardFrame(activeSide, leftKeyboard, rightKeyboard),
            commandLine,
            null,
            null,
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

    private ApplicationKeyboardFrame BuildKeyboardFrame(
        PanelSide activeSide,
        ApplicationPanelKeyboardFrame leftKeyboard,
        ApplicationPanelKeyboardFrame rightKeyboard) =>
        new(
            activeSide,
            _context.CommandLine.HasText,
            _context.CommandLine.HasSelection,
            leftKeyboard,
            rightKeyboard);

    private static ApplicationPanelFrame? WithKeyboard(
        ApplicationPanelFrame? frame,
        ApplicationPanelKeyboardFrame keyboard) =>
        frame is null
            ? null
            : new ApplicationPanelFrame(
                frame.Side,
                frame.Bounds,
                frame.VisibleRows,
                frame.VisibleItems,
                frame.RetryBounds,
                frame.ScrollBar,
                keyboard,
                frame.RowsPerColumn,
                frame.ColumnCount);
}
