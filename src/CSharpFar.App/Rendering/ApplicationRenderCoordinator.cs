using CSharpFar.App.State;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

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
        FilePanelState leftState = _context.LeftPanel();
        FilePanelState rightState = _context.RightPanel();
        FilePanelState activeState = activeSide == PanelSide.Left ? leftState : rightState;
        ApplicationPanelKeyboardFrame leftKeyboard = ApplicationPanelKeyboardSnapshot.Capture(leftState);
        ApplicationPanelKeyboardFrame rightKeyboard = ApplicationPanelKeyboardSnapshot.Capture(rightState);
        var workspace = _panelWorkspaceRenderer.Render(
            size,
            leftState,
            rightState,
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
            workspace.LeftPanel,
            workspace.RightPanel,
            functionKeyBar,
            directoryShortcutBar);
    }

    public ApplicationUiFrame RenderHiddenCommandLineContent(UiRenderContext context)
    {
        var viewport = context.Viewport;
        var size = context.Size;
        int row = ApplicationLayoutService.CommandLineRow(size);
        PanelSide activeSide = _context.ActiveSide();
        FilePanelState leftState = _context.LeftPanel();
        FilePanelState rightState = _context.RightPanel();
        FilePanelState activeState = activeSide == PanelSide.Left ? leftState : rightState;
        ApplicationPanelKeyboardFrame leftKeyboard = ApplicationPanelKeyboardSnapshot.Capture(leftState);
        ApplicationPanelKeyboardFrame rightKeyboard = ApplicationPanelKeyboardSnapshot.Capture(rightState);
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

}
