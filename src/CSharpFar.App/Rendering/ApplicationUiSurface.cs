using CSharpFar.App.CommandLine;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed record ApplicationUiFrame(
    ConsoleViewport Viewport,
    ApplicationWorkspaceMode Mode,
    ApplicationKeyboardFrame Keyboard,
    ApplicationCommandLineFrame CommandLine,
    ApplicationPanelFrame? LeftPanel,
    ApplicationPanelFrame? RightPanel,
    ApplicationFunctionKeyBarFrame? FunctionKeyBar,
    ApplicationDirectoryShortcutBarFrame? DirectoryShortcutBar)
{
    public ApplicationRenderFingerprint? Fingerprint { get; init; }
    public ApplicationRenderPart RenderedParts { get; init; } = ApplicationRenderPart.Full;
}

internal sealed record ApplicationRenderFingerprint(
    ConsoleViewport Viewport,
    ConsoleSize Size,
    ApplicationWorkspaceMode Mode,
    ApplicationCommandLineFingerprint CommandLine,
    FunctionKeys.FunctionKeyLayer FunctionKeyLayer,
    ApplicationClockFrame? Clock);

internal sealed record ApplicationCommandLineFingerprint(
    string CurrentDirectory,
    string Text,
    int CursorPosition,
    int? SelectionStart,
    int SelectionLength,
    ApplicationCommandLineFrame Frame);

[Flags]
internal enum ApplicationRenderPart
{
    None = 0,
    Clock = 1 << 0,
    CommandLine = 1 << 1,
    CommandLineCursor = 1 << 2,
    FunctionKeyBar = 1 << 3,
    Completion = 1 << 4,
    Full = 1 << 30,
}

internal sealed record ApplicationKeyboardFrame(
    PanelSide ActiveSide,
    bool CommandLineHasText,
    bool CommandLineHasSelection,
    ApplicationPanelKeyboardFrame LeftPanel,
    ApplicationPanelKeyboardFrame RightPanel)
{
    public ApplicationPanelKeyboardFrame ActivePanel =>
        Panel(ActiveSide);

    public bool ActivePanelHasSearchRequest =>
        ActivePanel.HasSearchRequest;

    public ApplicationPanelKeyboardFrame Panel(PanelSide side) =>
        side == PanelSide.Left ? LeftPanel : RightPanel;
}

internal sealed record ApplicationPanelKeyboardFrame(
    PanelLocation CurrentLocation,
    bool HasSearchRequest,
    int? CurrentItemIndex,
    PanelLocation? CurrentItemLocation,
    string? CurrentItemName)
{
    public string CurrentDirectory => CurrentLocation.SourcePath;
    public string? CurrentItemFullPath => CurrentItemLocation?.SourcePath;
}

internal static class ApplicationPanelKeyboardSnapshot
{
    public static ApplicationPanelKeyboardFrame Capture(FilePanelState state)
    {
        FilePanelItem? current = state.CursorIndex >= 0 && state.CursorIndex < state.Items.Count
            ? state.Items[state.CursorIndex]
            : null;
        return new ApplicationPanelKeyboardFrame(
            state.CurrentLocation,
            state.SearchRequest is not null,
            current is null ? null : state.CursorIndex,
            current?.Location,
            current?.Name);
    }
}

internal sealed record ApplicationCommandLineFrame(
    Rect Bounds,
    int PromptLength,
    int DisplayOffset,
    int TextLength,
    UiCursorPlacement? Cursor)
{
    public int TextPositionFromX(int x)
    {
        if (Bounds.Width <= 0)
            return 0;

        int clampedX = Math.Clamp(x, Bounds.X, Bounds.Right - 1);
        return Math.Clamp(clampedX + DisplayOffset - PromptLength, 0, TextLength);
    }
}

internal static class ApplicationTargetIds
{
    public static UiTargetId WorkspaceKeyboard { get; } = new("application.workspace-keyboard");
    public static UiTargetId CommandLine { get; } = new("application.command-line");
    public static UiTargetId LeftPanel { get; } = new("application.left-panel");
    public static UiTargetId LeftPanelScrollbar { get; } = new("application.left-panel.scrollbar");
    public static UiTargetId RightPanel { get; } = new("application.right-panel");
    public static UiTargetId RightPanelScrollbar { get; } = new("application.right-panel.scrollbar");
    private static UiTargetId LeftPanelRetry { get; } = new("application.left-panel.retry");
    private static UiTargetId RightPanelRetry { get; } = new("application.right-panel.retry");

    public static UiTargetId Panel(PanelSide side) =>
        side == PanelSide.Left ? LeftPanel : RightPanel;

    public static UiTargetId PanelScrollbar(PanelSide side) =>
        side == PanelSide.Left ? LeftPanelScrollbar : RightPanelScrollbar;

    public static UiTargetId PanelRetry(PanelSide side) =>
        side == PanelSide.Left ? LeftPanelRetry : RightPanelRetry;

    public static UiTargetId PanelItem(PanelSide side, int itemIndex) =>
        new($"application.{(side == PanelSide.Left ? "left" : "right")}-panel.item:{itemIndex}");

    public static UiTargetId FunctionKeyAction(FunctionKeys.FunctionKeyLayer layer, ConsoleKey key) =>
        new($"application.function-key:{layer}:{key}");

    public static UiTargetId DirectoryShortcut(int shortcutNumber) =>
        new($"application.directory-shortcut:{shortcutNumber}");
}

internal sealed record ApplicationPanelFrame
{
    public ApplicationPanelFrame(
        PanelSide side,
        Rect bounds,
        int visibleRows,
        IReadOnlyList<ApplicationPanelItemHit> visibleItems,
        Rect? retryBounds,
        ApplicationScrollBarFrame? scrollBar,
        int rowsPerColumn = 0,
        int columnCount = 1)
    {
        ArgumentNullException.ThrowIfNull(visibleItems);

        Side = side;
        Bounds = bounds;
        VisibleRows = visibleRows;
        VisibleItems = Array.AsReadOnly(visibleItems.ToArray());
        RetryBounds = retryBounds;
        ScrollBar = scrollBar;
        RowsPerColumn = rowsPerColumn > 0 ? rowsPerColumn : Math.Max(1, visibleRows);
        ColumnCount = Math.Max(1, columnCount);
    }

    public PanelSide Side { get; }
    public Rect Bounds { get; }
    public int VisibleRows { get; }
    public IReadOnlyList<ApplicationPanelItemHit> VisibleItems { get; }
    public Rect? RetryBounds { get; }
    public ApplicationScrollBarFrame? ScrollBar { get; }
    public int RowsPerColumn { get; }
    public int ColumnCount { get; }

    public bool OwnsPointerTarget(UiTargetId? target) =>
        target == ApplicationTargetIds.Panel(Side) ||
        target == ApplicationTargetIds.PanelRetry(Side) ||
        VisibleItems.Any(item => ApplicationTargetIds.PanelItem(Side, item.ItemIndex) == target);

    public bool IsRetryTarget(UiTargetId? target) =>
        RetryBounds is not null && target == ApplicationTargetIds.PanelRetry(Side);

    public bool TryGetItemTarget(UiTargetId? target, out ApplicationPanelItemHit hit)
    {
        ApplicationPanelItemHit? found = target is null
            ? null
            : VisibleItems.FirstOrDefault(item => ApplicationTargetIds.PanelItem(Side, item.ItemIndex) == target);
        hit = found!;
        return found is not null;
    }
}

internal sealed record ApplicationPanelItemHit(
    Rect Bounds,
    int ItemIndex,
    PanelLocation ItemLocation);

internal sealed record ApplicationScrollBarFrame(
    Rect Bounds,
    int TotalItems,
    int ViewportItems,
    int FirstVisibleIndex,
    VerticalScrollbarFrame? VerticalScrollbarFrame = null)
{
    public ScrollState ToScrollState() => new()
    {
        TotalItems = TotalItems,
        ViewportItems = ViewportItems,
        FirstVisibleIndex = FirstVisibleIndex,
    };
}

internal sealed record ApplicationScrollbarInput(
    PanelSide Side,
    int ViewportItems,
    VerticalScrollbarInputResult Result);

internal sealed record ApplicationUiInputPacket(
    UiRoutedInput<ApplicationUiFrame> Routed,
    ApplicationScrollbarInput? ScrollbarInput = null)
{
    public ConsoleInputEvent Input => Routed.Input;
    public ApplicationUiFrame Frame => Routed.Frame;
    public UiTargetId? Target => Routed.Target;
    public UiInputRouteKind RouteKind => Routed.RouteKind;
}

internal sealed record ApplicationFunctionKeyBarFrame
{
    public ApplicationFunctionKeyBarFrame(IReadOnlyList<ApplicationFunctionKeyHit> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        Actions = Array.AsReadOnly(actions.ToArray());
    }

    public IReadOnlyList<ApplicationFunctionKeyHit> Actions { get; }

    public bool TryGetPointerAction(UiTargetId? target, out ApplicationFunctionKeyHit action)
    {
        ApplicationFunctionKeyHit? found = target is null
            ? null
            : Actions.FirstOrDefault(candidate =>
                candidate.Bounds.Width > 0 &&
                candidate.Bounds.Height > 0 &&
                ApplicationTargetIds.FunctionKeyAction(candidate.Layer, candidate.Key) == target);
        action = found!;
        return found is not null;
    }
}

internal sealed record ApplicationFunctionKeyHit(
    Rect Bounds,
    string CommandId,
    FunctionKeys.FunctionKeyLayer Layer = FunctionKeys.FunctionKeyLayer.Plain,
    ConsoleKey Key = ConsoleKey.NoName,
    bool RunsWhenUnavailable = false);

internal sealed record ApplicationDirectoryShortcutBarFrame
{
    public ApplicationDirectoryShortcutBarFrame(IReadOnlyList<ApplicationDirectoryShortcutHit> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        Shortcuts = Array.AsReadOnly(shortcuts.ToArray());
    }

    public IReadOnlyList<ApplicationDirectoryShortcutHit> Shortcuts { get; }

    public bool TryGetPointerShortcut(UiTargetId? target, out ApplicationDirectoryShortcutHit shortcut)
    {
        ApplicationDirectoryShortcutHit? found = target is null
            ? null
            : Shortcuts.FirstOrDefault(candidate =>
                ApplicationTargetIds.DirectoryShortcut(candidate.ShortcutNumber) == target);
        shortcut = found!;
        return found is not null;
    }
}

internal sealed record ApplicationDirectoryShortcutHit(
    Rect Bounds,
    int ShortcutNumber,
    string Path);

internal sealed class ApplicationUiSurface : UiLayer<ApplicationUiFrame>, IUiSurface
{
    private readonly ApplicationRenderContext _context;
    private readonly ApplicationRenderCoordinator _coordinator;
    private readonly ScreenRenderer _screen;
    private readonly VerticalScrollbarController _leftScrollbar = new();
    private readonly VerticalScrollbarController _rightScrollbar = new();
    private readonly PendingInvalidation<ApplicationRenderPart> _invalidation =
        new(ApplicationRenderPart.Full);
    private bool _hidden;
    private ApplicationUiInputPacket? _pendingInput;

    public ApplicationUiSurface(ScreenRenderer screen, ApplicationRenderContext context, ApplicationRenderCoordinator coordinator)
    {
        _screen = screen;
        _context = context;
        _coordinator = coordinator;
        _invalidation.RequestFull();
    }

    public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;

    public bool TryAcceptViewportChange(ConsoleViewport viewport, ConsoleViewportChange change)
    {
        if (_context.App.WorkspaceMode != ApplicationWorkspaceMode.HiddenCommandLine ||
            !_context.TerminalSurface.TryAcceptViewportChange(viewport, change))
        {
            return false;
        }

        _context.Ui.HiddenUiDetachedByScroll = !_context.TerminalSurface.IsHiddenViewportPinnedToBottom;
        if (_context.Ui.HiddenUiDetachedByScroll)
        {
            _context.CommandCompletion.CloseForHiddenScroll();
            _screen.SetCursorVisible(false);
        }
        return true;
    }

    public IDisposable BeginFrame(UiRenderRequest request)
    {
        _hidden = _context.App.WorkspaceMode == ApplicationWorkspaceMode.HiddenCommandLine;
        if (!_hidden)
            _context.Ui.HiddenUiDetachedByScroll = false;
        _context.TerminalSurface.ApplyMode();
        _screen.SetRenderingOutputMode(true);

        if (!_hidden)
            return _screen.BeginFrame();

        if (request.IsResizeRecovery)
        {
            if (_context.TerminalSurface.UsesTerminalScreenMode)
                _context.TerminalSurface.PrepareHiddenResize();
            else
                _context.TerminalSurface.RestoreHiddenScreen();
        }

        var viewport = _screen.GetViewport();
        var row = ApplicationLayoutService.CommandLineRow(viewport.Size);
        var overlayBounds = new Rect(0, row, viewport.Width, 1);
        CommandCompletionState completion = _context.CommandCompletion;
        if (completion.Visible)
        {
            CommandCompletionLayoutFrame layout = CommandCompletionLayout.Calculate(viewport.Size, completion.List.Count);
            if (layout.IsVisible)
                overlayBounds = Union(overlayBounds, layout.PopupBounds);
        }
        _context.TerminalSurface.PrepareHiddenOverlay(viewport, overlayBounds);
        return _context.TerminalSurface.UsesTerminalScreenMode
            ? _screen.BeginFrameFromCurrentViewportCapture()
            : _screen.BeginFrame();
    }

    protected override ApplicationUiFrame RenderFrame(UiRenderContext context)
    {
        PendingInvalidationSnapshot<ApplicationRenderPart> attempt =
            _invalidation.SnapshotForRenderAttempt();
        ApplicationRenderPart parts = attempt.Parts;
        ApplicationWorkspaceMode mode = _hidden
            ? ApplicationWorkspaceMode.HiddenCommandLine
            : ApplicationWorkspaceMode.Panels;
        bool full = !HasCommittedFrame ||
            parts == ApplicationRenderPart.None ||
            CommittedFrame.Fingerprint is null ||
            CommittedFrame.Viewport != context.Viewport ||
            CommittedFrame.Mode != mode;

        ApplicationUiFrame frame;
        if (full || parts.HasFlag(ApplicationRenderPart.Full))
        {
            frame = _hidden
                ? _coordinator.RenderHiddenCommandLineContent(context)
                : _coordinator.RenderMainContent(context);
            frame = AttachScrollbarFrames(frame);
        }
        else
        {
            frame = _coordinator.RenderPartial(context, CommittedFrame, parts);
        }

        context.PublishOnStable(attempt, _invalidation.Commit);
        return frame;
    }

    public void RequestRender(ApplicationRenderPart parts)
    {
        if (parts != ApplicationRenderPart.None)
            _invalidation.Request(parts.HasFlag(ApplicationRenderPart.Completion)
                ? ApplicationRenderPart.Full
                : parts);
    }

    public void CompleteFrame(UiFrameCompletion completion)
    {
        if (_hidden && completion.WasCommitted)
        {
            _context.TerminalSurface.MarkHiddenCommandLineRenderCompleted();
            _context.Ui.HiddenUiDetachedByScroll = !_context.TerminalSurface.IsHiddenViewportPinnedToBottom;
        }
    }

    protected override UiInputResult RouteInput(
        ConsoleInputEvent input,
        ApplicationUiFrame frame,
        UiInputRouteContext context)
    {
        if (input is not (
            KeyConsoleInputEvent or
            ModifierKeyConsoleInputEvent or
            MouseConsoleInputEvent))
        {
            return UiInputResult.NotHandled;
        }

        if (_context.Ui.HiddenUiDetachedByScroll && input is MouseConsoleInputEvent)
            return UiInputResult.NotHandled;

        if (_pendingInput is not null)
            throw new InvalidOperationException("Application input was dispatched before the previous input was processed.");

        var routed = new UiRoutedInput<ApplicationUiFrame>(input, frame, context.Target, context.RouteKind);
        if (input is MouseConsoleInputEvent mouse &&
            TryRouteScrollbarMouse(mouse, frame, context, out ApplicationScrollbarInput? scrollbarInput, out UiInputResult scrollbarResult))
        {
            _pendingInput = new ApplicationUiInputPacket(routed, scrollbarInput);
            return scrollbarResult;
        }

        _pendingInput = new ApplicationUiInputPacket(routed);

        if (context.Target == ApplicationTargetIds.CommandLine &&
            input is MouseConsoleInputEvent { Button: MouseButton.Left, Kind: MouseEventKind.Down } &&
            context.RouteKind == UiInputRouteKind.HitTarget)
        {
            return UiInputResult.CaptureMouse(ApplicationTargetIds.CommandLine, MouseButton.Left);
        }

        if (context.Target == ApplicationTargetIds.CommandLine &&
            input is MouseConsoleInputEvent { Button: MouseButton.Left, Kind: MouseEventKind.Up } &&
            context.RouteKind == UiInputRouteKind.CapturedTarget)
        {
            return UiInputResult.ReleaseMouse();
        }

        return UiInputResult.HandledResult;
    }

    protected override UiInteractionFrame BuildInteractionFrame(ApplicationUiFrame frame)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddFocusEntry(ApplicationTargetIds.CommandLine, 0, cursor: frame.CommandLine.Cursor)
            .SetDefaultFocusTarget(ApplicationTargetIds.CommandLine)
            .SetKeyboardTarget(ApplicationTargetIds.WorkspaceKeyboard);
        if (frame.Mode == ApplicationWorkspaceMode.Panels)
        {
            if (frame.LeftPanel is not null)
                builder.AddFocusEntry(ApplicationTargetIds.LeftPanel, 1);
            if (frame.RightPanel is not null)
                builder.AddFocusEntry(ApplicationTargetIds.RightPanel, 2);
        }
        if (IsVisible(frame.CommandLine.Bounds, frame.Viewport))
            builder.AddHitRegion(ApplicationTargetIds.CommandLine, frame.CommandLine.Bounds);

        if (frame.Mode == ApplicationWorkspaceMode.Panels)
        {
            AddPanelRegions(builder, frame.LeftPanel, frame.Viewport);
            AddPanelRegions(builder, frame.RightPanel, frame.Viewport);

            if (frame.FunctionKeyBar is { } functionKeyBar)
            {
                foreach (ApplicationFunctionKeyHit action in functionKeyBar.Actions)
                {
                    if (IsVisible(action.Bounds, frame.Viewport))
                    {
                        builder.AddHitRegion(
                            ApplicationTargetIds.FunctionKeyAction(action.Layer, action.Key),
                            action.Bounds);
                    }
                }
            }

            if (frame.DirectoryShortcutBar is { } shortcutBar)
            {
                foreach (ApplicationDirectoryShortcutHit shortcut in shortcutBar.Shortcuts)
                {
                    if (IsVisible(shortcut.Bounds, frame.Viewport))
                    {
                        builder.AddHitRegion(
                            ApplicationTargetIds.DirectoryShortcut(shortcut.ShortcutNumber),
                            shortcut.Bounds);
                    }
                }
            }
        }

        return builder.Build();
    }

    protected override void OnFrameCommitted(ApplicationUiFrame frame)
    {
        _leftScrollbar.ApplyCommittedFrame(frame.LeftPanel?.ScrollBar?.VerticalScrollbarFrame);
        _rightScrollbar.ApplyCommittedFrame(frame.RightPanel?.ScrollBar?.VerticalScrollbarFrame);
    }

    private ApplicationUiFrame AttachScrollbarFrames(ApplicationUiFrame frame) =>
        frame with
        {
            LeftPanel = AttachScrollbarFrame(frame.LeftPanel, _leftScrollbar),
            RightPanel = AttachScrollbarFrame(frame.RightPanel, _rightScrollbar),
        };

    private static ApplicationPanelFrame? AttachScrollbarFrame(
        ApplicationPanelFrame? panel,
        VerticalScrollbarController controller)
    {
        if (panel?.ScrollBar is not { } scrollbar)
            return panel;

        VerticalScrollbarFrame? scrollbarFrame = controller.CalculateFrame(
            scrollbar.Bounds,
            scrollbar.ToScrollState());
        var updatedScrollbar = scrollbar with { VerticalScrollbarFrame = scrollbarFrame };
        return new ApplicationPanelFrame(
            panel.Side,
            panel.Bounds,
            panel.VisibleRows,
            panel.VisibleItems,
            panel.RetryBounds,
            updatedScrollbar,
            panel.RowsPerColumn,
            panel.ColumnCount);
    }

    private static void AddPanelRegions(
        UiInteractionFrameBuilder builder,
        ApplicationPanelFrame? panel,
        ConsoleViewport viewport)
    {
        if (panel is null)
            return;

        if (IsVisible(panel.Bounds, viewport))
            builder.AddHitRegion(ApplicationTargetIds.Panel(panel.Side), panel.Bounds);

        if (panel.RetryBounds is { } retryBounds && IsVisible(retryBounds, viewport))
            builder.AddHitRegion(ApplicationTargetIds.PanelRetry(panel.Side), retryBounds);

        foreach (ApplicationPanelItemHit item in panel.VisibleItems)
        {
            if (!IsVisible(item.Bounds, viewport))
                continue;

            UiTargetId target = ApplicationTargetIds.PanelItem(panel.Side, item.ItemIndex);
            builder.AddHitRegion(target, item.Bounds);

            // The left border of the right panel is the shared panel separator. Historically
            // clicking it selected the first-column row immediately to its right.
            if (panel.Side == PanelSide.Right && item.Bounds.X == panel.Bounds.X + 1)
                builder.AddHitRegion(target, new Rect(panel.Bounds.X, item.Bounds.Y, 1, item.Bounds.Height));
        }

        if (panel.ScrollBar is { } scrollbar &&
            IsVisible(scrollbar.Bounds, viewport) &&
            scrollbar.VerticalScrollbarFrame is not null)
        {
            builder.AddHitRegion(ApplicationTargetIds.PanelScrollbar(panel.Side), scrollbar.Bounds);
        }
    }

    private static bool IsVisible(Rect bounds, ConsoleViewport viewport) =>
        bounds.Width > 0 &&
        bounds.Height > 0 &&
        bounds.Right > 0 &&
        bounds.Bottom > 0 &&
        bounds.X < viewport.Width &&
        bounds.Y < viewport.Height;

    private static Rect Union(Rect first, Rect second)
    {
        int left = Math.Min(first.X, second.X);
        int top = Math.Min(first.Y, second.Y);
        int right = Math.Max(first.Right, second.Right);
        int bottom = Math.Max(first.Bottom, second.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private bool TryRouteScrollbarMouse(
        MouseConsoleInputEvent mouse,
        ApplicationUiFrame frame,
        UiInputRouteContext context,
        out ApplicationScrollbarInput? scrollbarInput,
        out UiInputResult uiResult)
    {
        scrollbarInput = null;
        uiResult = UiInputResult.NotHandled;
        if (!TryGetScrollbarTarget(context.Target, out PanelSide side))
            return false;
        if (context.RouteKind is not (UiInputRouteKind.HitTarget or UiInputRouteKind.CapturedTarget))
            return false;

        ApplicationScrollBarFrame? scrollbar = side == PanelSide.Left
            ? frame.LeftPanel?.ScrollBar
            : frame.RightPanel?.ScrollBar;
        if (scrollbar?.VerticalScrollbarFrame is not { } scrollbarFrame)
        {
            if (context.RouteKind == UiInputRouteKind.CapturedTarget &&
                mouse.Kind == MouseEventKind.Up &&
                mouse.Button == MouseButton.Left)
            {
                uiResult = UiInputResult.ReleaseMouse();
                return true;
            }

            return false;
        }

        VerticalScrollbarController controller = side == PanelSide.Left
            ? _leftScrollbar
            : _rightScrollbar;
        VerticalScrollbarInputResult result = controller.HandleMouse(mouse, scrollbarFrame);
        if (!result.IsHandled)
            return false;

        scrollbarInput = new ApplicationScrollbarInput(side, scrollbar.ViewportItems, result);
        uiResult = VerticalScrollbarRouting.ToUiInputResult(
            result,
            ApplicationTargetIds.PanelScrollbar(side));
        return true;
    }

    private static bool TryGetScrollbarTarget(UiTargetId? target, out PanelSide side)
    {
        if (target == ApplicationTargetIds.LeftPanelScrollbar)
        {
            side = PanelSide.Left;
            return true;
        }

        if (target == ApplicationTargetIds.RightPanelScrollbar)
        {
            side = PanelSide.Right;
            return true;
        }

        side = default;
        return false;
    }

    internal bool TryTakeInput(out ApplicationUiInputPacket packet)
    {
        if (_pendingInput is null)
        {
            packet = null!;
            return false;
        }

        packet = _pendingInput;
        _pendingInput = null;
        return true;
    }
}
