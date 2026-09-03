using CSharpFar.App.FunctionKeys;
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

    public ApplicationUiFrame RenderMainContent(
        UiRenderContext context,
        Func<HoverMarqueeRegistration, string>? renderHoverMarquee = null)
    {
        UpdateQuickViewDirSize();
        _context.QuickViewDirectorySize.SynchronizeRecentChanges();
        var size = context.Size;
        PanelSide activeSide = _context.ActiveSide();
        FilePanelState leftState = _context.LeftPanel();
        FilePanelState rightState = _context.RightPanel();
        FilePanelState activeState = activeSide == PanelSide.Left ? leftState : rightState;
        FilePanelItem? activeItem = _context.PanelController.CurrentItem(activeState);
        _context.FileUsagePanel.Update(_context.App.FileUsage, activeState.SourceId, activeItem);
        ApplicationPanelKeyboardFrame leftKeyboard = ApplicationPanelKeyboardSnapshot.Capture(leftState);
        ApplicationPanelKeyboardFrame rightKeyboard = ApplicationPanelKeyboardSnapshot.Capture(rightState);
        var workspace = _panelWorkspaceRenderer.Render(
            context.Canvas,
            size,
            leftState,
            rightState,
            activeSide,
            _context.LeftViewMode(),
            _context.RightViewMode(),
            _context.App.QuickView,
            _context.App.FileUsage,
            _context.FileUsagePanel,
            _context.QuickViewDirectorySize.CurrentState,
            _context.QuickViewDirectorySize.Monitor,
            _context.QuickViewDirectorySize.SelectedMonitorChangeId,
            _context.QuickViewDirectorySize.IsBackgroundUpdating,
            _context.QuickViewDirectorySize.GetFirstVisibleMonitorChangeIndex(),
            _context.QuickViewDirectorySize.RecentChanges,
            renderHoverMarquee);
        if (workspace.QuickView is { } quickViewFrame)
        {
            context.PublishOnStable(() => _context.QuickViewDirectorySize.SetMonitorChanges(
                quickViewFrame.AllChangeIds,
                quickViewFrame.VisibleChangeIds,
                quickViewFrame.NormalizedSelectedChangeId,
                quickViewFrame.FirstVisibleChangeIndex));
            if (quickViewFrame.RecentChanges is { } recentChanges && quickViewFrame.RecentChangesFrame is { } recentChangesFrame)
                context.PublishOnStable(() => recentChanges.ApplyCommittedFrame(recentChangesFrame));
        }
        int panelHeight = workspace.PanelHeight;
        context.PublishOnStable(context.Viewport, value => _context.Ui.LastRenderViewport = value);

        ApplicationDirectoryShortcutBarFrame? directoryShortcutBar =
            new DirectoryShortcutBarRenderer(context.Canvas, _context.App.Palette)
                .Render(panelHeight - 1, size.Width, _context.DirectoryShortcuts());

        ApplicationClockFrame? clock = _clockRenderer.Render(context.Canvas, size);

        ApplicationCommandLineFrame commandLine = _commandLineRenderer.Render(
            context.Canvas,
            panelHeight,
            size,
            activeState.CurrentDirectory,
            _context.CommandLine);

        ApplicationFunctionKeyBarFrame? functionKeyBar =
            _functionKeyBarRenderer.Render(context.Canvas, size, _context.FunctionKeyLayer());

        return new ApplicationUiFrame(
            context.Viewport,
            ApplicationWorkspaceMode.Panels,
            BuildKeyboardFrame(activeSide, leftKeyboard, rightKeyboard),
            commandLine,
            workspace.LeftPanel,
            workspace.RightPanel,
            functionKeyBar,
            directoryShortcutBar)
        {
            QuickView = workspace.QuickView,
            FileUsage = workspace.FileUsage,
            Fingerprint = CreateFingerprint(
                context.Viewport,
                ApplicationWorkspaceMode.Panels,
                activeState.CurrentDirectory,
                commandLine,
                clock),
            RenderedParts = ApplicationRenderPart.Full,
        };
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
            context.Canvas,
            row,
            size,
            activeState.CurrentDirectory,
            _context.CommandLine);
        TracePromptWrite(context, commandLine, activeState.CurrentDirectory);

        return new ApplicationUiFrame(
            context.Viewport,
            ApplicationWorkspaceMode.HiddenCommandLine,
            BuildKeyboardFrame(activeSide, leftKeyboard, rightKeyboard),
            commandLine,
            null,
            null,
            null,
            null)
        {
            Fingerprint = CreateFingerprint(
                context.Viewport,
                ApplicationWorkspaceMode.HiddenCommandLine,
                activeState.CurrentDirectory,
                commandLine,
                clock: null),
            RenderedParts = ApplicationRenderPart.Full,
        };
    }

    public ApplicationUiFrame RenderPartial(
        UiRenderContext context,
        ApplicationUiFrame committed,
        ApplicationRenderPart requestedParts)
    {
        ApplicationRenderFingerprint previous = committed.Fingerprint ??
            throw new InvalidOperationException("A partial application render requires a committed fingerprint.");
        ApplicationRenderPart renderedParts = ApplicationRenderPart.None;
        ApplicationKeyboardFrame keyboard = committed.Keyboard;
        ApplicationCommandLineFrame commandLine = committed.CommandLine;
        ApplicationFunctionKeyBarFrame? functionKeyBar = committed.FunctionKeyBar;
        ApplicationCommandLineFingerprint commandFingerprint = previous.CommandLine;
        FunctionKeyLayer functionKeyLayer = previous.FunctionKeyLayer;
        ApplicationClockFrame? clock = previous.Clock;

        if ((requestedParts & (ApplicationRenderPart.CommandLine | ApplicationRenderPart.CommandLineCursor)) != 0)
        {
            ApplicationCommandLineFingerprint current = CaptureCommandLine(committed);
            bool cursorOnly =
                SameCommandLineExceptCursor(previous.CommandLine, current) &&
                previous.CommandLine.CursorPosition != current.CursorPosition;
            if (cursorOnly)
            {
                commandLine = current.Frame;
                commandFingerprint = current;
                renderedParts |= ApplicationRenderPart.CommandLineCursor;
            }
            else
            {
                _commandLineRenderer.Render(
                    context.Canvas,
                    current.Frame,
                    current.CurrentDirectory,
                    _context.CommandLine);
                commandLine = current.Frame;
                commandFingerprint = current;
                renderedParts |= ApplicationRenderPart.CommandLine;
            }

            keyboard = keyboard with
            {
                CommandLineHasText = _context.CommandLine.HasText,
                CommandLineHasSelection = _context.CommandLine.HasSelection,
            };
        }

        if (requestedParts.HasFlag(ApplicationRenderPart.FunctionKeyBar) &&
            committed.Mode == ApplicationWorkspaceMode.Panels)
        {
            functionKeyLayer = _context.FunctionKeyLayer();
            functionKeyBar = _functionKeyBarRenderer.Render(
                context.Canvas,
                context.Size,
                functionKeyLayer);
            renderedParts |= ApplicationRenderPart.FunctionKeyBar;
        }

        ApplicationClockFrame? currentClock = committed.Mode == ApplicationWorkspaceMode.Panels
            ? _clockRenderer.CreateFrame(context.Size)
            : null;
        if (requestedParts.HasFlag(ApplicationRenderPart.Clock) ||
            currentClock != previous.Clock)
        {
            clock = committed.Mode == ApplicationWorkspaceMode.Panels
                ? _clockRenderer.Render(context.Canvas, context.Size)
                : null;
            renderedParts |= ApplicationRenderPart.Clock;
        }

        return committed with
        {
            Keyboard = keyboard,
            CommandLine = commandLine,
            FunctionKeyBar = functionKeyBar,
            Fingerprint = previous with
            {
                CommandLine = commandFingerprint,
                FunctionKeyLayer = functionKeyLayer,
                Clock = clock,
            },
            RenderedParts = renderedParts,
        };
    }

    private ApplicationCommandLineFingerprint CaptureCommandLine(ApplicationUiFrame committed)
    {
        FilePanelState activeState = _context.ActiveSide() == PanelSide.Left
            ? _context.LeftPanel()
            : _context.RightPanel();
        string currentDirectory = activeState.CurrentDirectory;
        ApplicationCommandLineFrame frame = CommandLineLayoutCalculator.Calculate(
            committed.CommandLine.Bounds.Y,
            committed.Viewport.Width,
            currentDirectory,
            _context.CommandLine);
        return CreateCommandLineFingerprint(currentDirectory, frame);
    }

    private ApplicationRenderFingerprint CreateFingerprint(
        ConsoleViewport viewport,
        ApplicationWorkspaceMode mode,
        string currentDirectory,
        ApplicationCommandLineFrame commandLine,
        ApplicationClockFrame? clock) =>
        new(
            viewport,
            viewport.Size,
            mode,
            CreateCommandLineFingerprint(currentDirectory, commandLine),
            mode == ApplicationWorkspaceMode.Panels
                ? _context.FunctionKeyLayer()
                : FunctionKeyLayer.Plain,
            clock);

    private ApplicationCommandLineFingerprint CreateCommandLineFingerprint(
        string currentDirectory,
        ApplicationCommandLineFrame frame) =>
        new(
            currentDirectory,
            _context.CommandLine.Text,
            _context.CommandLine.CursorPosition,
            _context.CommandLine.SelectionStart,
            _context.CommandLine.SelectionLength,
            frame);

    private static bool SameCommandLineExceptCursor(
        ApplicationCommandLineFingerprint previous,
        ApplicationCommandLineFingerprint current) =>
        previous.CurrentDirectory == current.CurrentDirectory &&
        previous.Text == current.Text &&
        previous.SelectionStart == current.SelectionStart &&
        previous.SelectionLength == current.SelectionLength &&
        previous.Frame.Bounds.Equals(current.Frame.Bounds) &&
        previous.Frame.PromptLength == current.Frame.PromptLength &&
        previous.Frame.DisplayOffset == current.Frame.DisplayOffset &&
        previous.Frame.TextLength == current.Frame.TextLength;

    private static void TracePromptWrite(
        UiRenderContext context,
        ApplicationCommandLineFrame frame,
        string currentDirectory)
    {
        if (!HiddenResizeTrace.Enabled)
            return;

        int textLength = currentDirectory.Length + 1;
        HiddenResizeTrace.Write(
            $"PROMPT_WRITE viewportWidth={context.Viewport.Width} viewportHeight={context.Viewport.Height} row={frame.Bounds.Y} x={frame.Bounds.X} writeLength={frame.Bounds.Width} textLength={textLength}");
        if (frame.Cursor is { } cursor)
            HiddenResizeTrace.Write(
                $"PROMPT_CURSOR viewportWidth={context.Viewport.Width} viewportHeight={context.Viewport.Height} row={cursor.Y} x={cursor.X}");
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
