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
    public ApplicationQuickViewFrame? QuickView { get; init; }
    public ApplicationFileUsageFrame? FileUsage { get; init; }
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

    public static UiTargetId QuickViewMonitorToggle { get; } = new("application.quick-view.monitor-toggle");
    public static UiTargetId QuickViewChange(long changeId) => new($"application.quick-view.change:{changeId}");
    public static UiTargetId FileUsageOwner(int ownerIndex) => new($"application.file-usage.owner:{ownerIndex}");
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

internal sealed record ApplicationPanelPointerTarget(ApplicationPanelItemHit? Item = null, bool IsRetry = false);

internal abstract record ApplicationPointerInteraction;
internal sealed record ApplicationPanelInteraction(
    PanelSide Side,
    ApplicationPanelFrame Frame,
    RoutedPointerAction<ApplicationPanelPointerTarget> Action) : ApplicationPointerInteraction;
internal sealed record ApplicationPanelScrollInteraction(PanelSide Side, int ViewportItems, int FirstVisibleIndex) : ApplicationPointerInteraction;
internal sealed record ApplicationCommandLineInteraction(RoutedPointerSelectionAction<int> Action) : ApplicationPointerInteraction;
internal sealed record ApplicationFunctionKeyInteraction(ApplicationUiFrame Frame, ApplicationFunctionKeyHit Action) : ApplicationPointerInteraction;
internal sealed record ApplicationDirectoryShortcutInteraction(ApplicationDirectoryShortcutHit Shortcut, PanelSide Side) : ApplicationPointerInteraction;
internal abstract record ApplicationQuickViewPointerTarget;
internal sealed record ApplicationQuickViewMonitorToggleTarget : ApplicationQuickViewPointerTarget;
internal sealed record ApplicationQuickViewChangeTarget(long ChangeId) : ApplicationQuickViewPointerTarget;
internal sealed record ApplicationQuickViewPointerInteraction(ApplicationQuickViewPointerTarget Target) : ApplicationPointerInteraction;
internal sealed record ApplicationFileUsagePointerInteraction(int OwnerIndex) : ApplicationPointerInteraction;
internal sealed record ApplicationFileUsageOwnerHit(Rect Bounds, int OwnerIndex);
internal sealed record ApplicationFileUsageFrame(Rect Bounds, IReadOnlyList<ApplicationFileUsageOwnerHit> OwnerHits);
internal sealed record ApplicationQuickViewChangeHit(Rect Bounds, long ChangeId);
internal sealed record ApplicationQuickViewPointerHit(Rect Bounds, ApplicationQuickViewPointerTarget Target);
internal sealed record ApplicationQuickViewFrame(
    Rect Bounds,
    Rect? MonitorToggleBounds,
    IReadOnlyList<ApplicationQuickViewChangeHit> ChangeHits,
    long? NormalizedSelectedChangeId = null)
{
    public IReadOnlyList<long> VisibleChangeIds => ChangeHits.Select(hit => hit.ChangeId).ToArray();
    public IReadOnlyList<ApplicationQuickViewPointerHit> PointerHits =>
        (MonitorToggleBounds is { } monitorToggle
            ? new[] { new ApplicationQuickViewPointerHit(monitorToggle, new ApplicationQuickViewMonitorToggleTarget()) }
            : [])
        .Concat(ChangeHits.Select(hit => new ApplicationQuickViewPointerHit(hit.Bounds, new ApplicationQuickViewChangeTarget(hit.ChangeId))))
        .ToArray();
}

internal sealed record ApplicationUiInputPacket(
    UiRoutedInput<ApplicationUiFrame> Routed,
    ApplicationPointerInteraction? PointerInteraction = null)
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
    private readonly RoutedScrollbarSurface _leftPanelScrollbar =
        new(ApplicationTargetIds.LeftPanelScrollbar);
    private readonly RoutedScrollbarSurface _rightPanelScrollbar =
        new(ApplicationTargetIds.RightPanelScrollbar);
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
                _context.TerminalSurface.BeginHiddenResize();

            ConsoleViewport resizeViewport = _context.TerminalSurface.WaitForStableHiddenGeometry();
            HiddenResizeTrace.Write(
                $"Hidden recovery started viewport={HiddenResizeTrace.Viewport(resizeViewport)}");

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
            frame = AttachPanelInteractionFrames(frame);
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
        if (input is MouseConsoleInputEvent mouse)
        {
            RoutedApplicationPointerInput pointer = RoutePointerInput(mouse, frame, context);
            if (pointer.UiResult.Handled)
            {
                _pendingInput = new ApplicationUiInputPacket(routed, pointer.Interaction);
                return pointer.UiResult;
            }
        }

        _pendingInput = new ApplicationUiInputPacket(routed);
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
            builder.AddFragment(new RoutedPointerSelectionSurface<int>(ApplicationTargetIds.CommandLine, (_, _) => 0)
                .BuildInteractionFragment(frame.CommandLine.Bounds));

        if (frame.Mode == ApplicationWorkspaceMode.Panels)
        {
            AddPanelInteraction(builder, frame.LeftPanel, frame.Viewport);
            AddPanelInteraction(builder, frame.RightPanel, frame.Viewport);
            AddQuickViewInteraction(builder, frame.QuickView, frame.Viewport);
            AddFileUsageInteraction(builder, frame.FileUsage, frame.Viewport);

            if (frame.FunctionKeyBar is { } functionKeyBar)
            {
                builder.AddFragment(CreateFunctionKeyInteraction(functionKeyBar, frame.Viewport));
            }

            if (frame.DirectoryShortcutBar is { } shortcutBar)
            {
                builder.AddFragment(CreateShortcutInteraction(shortcutBar, frame.Viewport));
            }
        }

        return builder.Build();
    }

    protected override void OnFrameCommitted(ApplicationUiFrame frame)
    {
        _leftPanelScrollbar.ApplyCommittedFrame(frame.LeftPanel?.ScrollBar?.VerticalScrollbarFrame);
        _rightPanelScrollbar.ApplyCommittedFrame(frame.RightPanel?.ScrollBar?.VerticalScrollbarFrame);
    }

    private ApplicationUiFrame AttachPanelInteractionFrames(ApplicationUiFrame frame) =>
        frame with
        {
            LeftPanel = AttachPanelInteractionFrame(frame.LeftPanel, _leftPanelScrollbar),
            RightPanel = AttachPanelInteractionFrame(frame.RightPanel, _rightPanelScrollbar),
        };

    private static ApplicationPanelFrame? AttachPanelInteractionFrame(
        ApplicationPanelFrame? panel,
        RoutedScrollbarSurface scrollbarSurface)
    {
        if (panel?.ScrollBar is not { } scrollbar)
            return panel;

        VerticalScrollbarFrame? scrollbarFrame = scrollbarSurface.CalculateFrame(
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

    private void AddPanelInteraction(
        UiInteractionFrameBuilder builder,
        ApplicationPanelFrame? panel,
        ConsoleViewport viewport)
    {
        if (panel is null)
            return;

        var items = panel.VisibleItems
            .Where(item => IsVisible(item.Bounds, viewport))
            .Select(item => new RoutedPointerItem<ApplicationPanelPointerTarget>(new(item), item.Bounds))
            .ToList();
        if (panel.RetryBounds is { } retryBounds && IsVisible(retryBounds, viewport))
            items.Add(new RoutedPointerItem<ApplicationPanelPointerTarget>(new(IsRetry: true), retryBounds));
        // The left border of the right panel is the shared separator and remains clickable as its first column.
        if (panel.Side == PanelSide.Right)
        {
            foreach (ApplicationPanelItemHit item in panel.VisibleItems.Where(item =>
                         item.Bounds.X == panel.Bounds.X + 1 && IsVisible(item.Bounds, viewport)))
                items.Add(new RoutedPointerItem<ApplicationPanelPointerTarget>(new(item), new Rect(panel.Bounds.X, item.Bounds.Y, 1, item.Bounds.Height)));
        }
        var pointerItems = new RoutedPointerCollection<ApplicationPanelPointerTarget>(
            ApplicationTargetIds.Panel(panel.Side),
            target => target.IsRetry
                ? ApplicationTargetIds.PanelRetry(panel.Side)
                : ApplicationTargetIds.PanelItem(panel.Side, target.Item!.ItemIndex));
        builder.AddFragment(pointerItems.BuildInteractionFragment(
            IsVisible(panel.Bounds, viewport) ? panel.Bounds : new Rect(0, 0, 0, 0),
            items));
        RoutedScrollbarSurface scrollbarSurface = panel.Side == PanelSide.Left ? _leftPanelScrollbar : _rightPanelScrollbar;
        builder.AddFragment(scrollbarSurface.BuildInteractionFragment(
            panel.ScrollBar is { } scrollbar && IsVisible(scrollbar.Bounds, viewport) ? scrollbar.Bounds : null,
            panel.ScrollBar?.VerticalScrollbarFrame));
    }

    private static UiInteractionFragment CreateFunctionKeyInteraction(ApplicationFunctionKeyBarFrame frame, ConsoleViewport viewport)
    {
        var collection = new RoutedPointerCollection<ApplicationFunctionKeyHit>(
            new UiTargetId("application.function-key-bar"),
            action => ApplicationTargetIds.FunctionKeyAction(action.Layer, action.Key));
        return collection.BuildInteractionFragment(
            new Rect(0, 0, 0, 0),
            frame.Actions.Where(action => IsVisible(action.Bounds, viewport)).Select(action => new RoutedPointerItem<ApplicationFunctionKeyHit>(action, action.Bounds)).ToArray());
    }

    private static void AddQuickViewInteraction(UiInteractionFrameBuilder builder, ApplicationQuickViewFrame? frame, ConsoleViewport viewport)
    {
        if (frame is null)
            return;
        var collection = new RoutedPointerCollection<ApplicationQuickViewPointerHit>(
            new UiTargetId("application.quick-view"), hit => hit.Target switch
            {
                ApplicationQuickViewMonitorToggleTarget => ApplicationTargetIds.QuickViewMonitorToggle,
                ApplicationQuickViewChangeTarget change => ApplicationTargetIds.QuickViewChange(change.ChangeId),
                _ => throw new InvalidOperationException("Unknown Quick View pointer target."),
            });
        builder.AddFragment(collection.BuildInteractionFragment(
            IsVisible(frame.Bounds, viewport) ? frame.Bounds : new Rect(0, 0, 0, 0),
            frame.PointerHits.Where(hit => IsVisible(hit.Bounds, viewport)).Select(hit => new RoutedPointerItem<ApplicationQuickViewPointerHit>(hit, hit.Bounds)).ToArray()));
    }

    private static void AddFileUsageInteraction(UiInteractionFrameBuilder builder, ApplicationFileUsageFrame? frame, ConsoleViewport viewport)
    {
        if (frame is null) return;
        var collection = new RoutedPointerCollection<ApplicationFileUsageOwnerHit>(
            new UiTargetId("application.file-usage"), hit => ApplicationTargetIds.FileUsageOwner(hit.OwnerIndex));
        builder.AddFragment(collection.BuildInteractionFragment(
            IsVisible(frame.Bounds, viewport) ? frame.Bounds : new Rect(0, 0, 0, 0),
            frame.OwnerHits.Where(hit => IsVisible(hit.Bounds, viewport))
                .Select(hit => new RoutedPointerItem<ApplicationFileUsageOwnerHit>(hit, hit.Bounds)).ToArray()));
    }

    private static UiInteractionFragment CreateShortcutInteraction(ApplicationDirectoryShortcutBarFrame frame, ConsoleViewport viewport)
    {
        var collection = new RoutedPointerCollection<ApplicationDirectoryShortcutHit>(
            new UiTargetId("application.directory-shortcut-bar"),
            shortcut => ApplicationTargetIds.DirectoryShortcut(shortcut.ShortcutNumber));
        return collection.BuildInteractionFragment(
            new Rect(0, 0, 0, 0),
            frame.Shortcuts.Where(shortcut => IsVisible(shortcut.Bounds, viewport)).Select(shortcut => new RoutedPointerItem<ApplicationDirectoryShortcutHit>(shortcut, shortcut.Bounds)).ToArray());
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

    private RoutedApplicationPointerInput RoutePointerInput(
        MouseConsoleInputEvent input,
        ApplicationUiFrame frame,
        UiInputRouteContext context)
    {
        RoutedPointerSelectionInput<int> commandLine = new RoutedPointerSelectionSurface<int>(
            ApplicationTargetIds.CommandLine,
            (x, _) => frame.CommandLine.TextPositionFromX(x)).RouteInput(input, context);
        if (commandLine.UiResult.Handled)
            return new(new ApplicationCommandLineInteraction(commandLine.Action), commandLine.UiResult);

        if (TryRouteScrollbar(input, frame.LeftPanel, PanelSide.Left, _leftPanelScrollbar, context, out RoutedApplicationPointerInput leftScrollbar))
            return leftScrollbar;
        if (TryRouteScrollbar(input, frame.RightPanel, PanelSide.Right, _rightPanelScrollbar, context, out RoutedApplicationPointerInput rightScrollbar))
            return rightScrollbar;
        if (TryRoutePanel(input, frame.LeftPanel, context, out RoutedApplicationPointerInput leftPanel))
            return leftPanel;
        if (TryRoutePanel(input, frame.RightPanel, context, out RoutedApplicationPointerInput rightPanel))
            return rightPanel;

        if (frame.QuickView is { } quickView)
        {
            var collection = new RoutedPointerCollection<ApplicationQuickViewPointerHit>(new("application.quick-view"), hit => hit.Target switch
            {
                ApplicationQuickViewMonitorToggleTarget => ApplicationTargetIds.QuickViewMonitorToggle,
                ApplicationQuickViewChangeTarget change => ApplicationTargetIds.QuickViewChange(change.ChangeId),
                _ => throw new InvalidOperationException("Unknown Quick View pointer target."),
            });
            RoutedPointerInput<ApplicationQuickViewPointerHit> action = collection.RouteInput(input, context, quickView.PointerHits.Select(hit => new RoutedPointerItem<ApplicationQuickViewPointerHit>(hit, hit.Bounds)).ToArray());
            if (action.UiResult.Handled && action.Action.Kind == RoutedPointerActionKind.ItemPrimaryPressed)
                return new(new ApplicationQuickViewPointerInteraction(action.Action.Item!.Target), action.UiResult);
        }

        if (frame.FileUsage is { } fileUsage)
        {
            var collection = new RoutedPointerCollection<ApplicationFileUsageOwnerHit>(
                new("application.file-usage"), hit => ApplicationTargetIds.FileUsageOwner(hit.OwnerIndex));
            RoutedPointerInput<ApplicationFileUsageOwnerHit> action = collection.RouteInput(input, context,
                fileUsage.OwnerHits.Select(hit => new RoutedPointerItem<ApplicationFileUsageOwnerHit>(hit, hit.Bounds)).ToArray());
            if (action.UiResult.Handled && action.Action.Kind == RoutedPointerActionKind.ItemPrimaryPressed)
                return new(new ApplicationFileUsagePointerInteraction(action.Action.Item!.OwnerIndex), action.UiResult);
        }

        if (frame.FunctionKeyBar is { } functionKeys)
        {
            var collection = new RoutedPointerCollection<ApplicationFunctionKeyHit>(new("application.function-key-bar"), action => ApplicationTargetIds.FunctionKeyAction(action.Layer, action.Key));
            RoutedPointerInput<ApplicationFunctionKeyHit> action = collection.RouteInput(input, context, functionKeys.Actions.Select(x => new RoutedPointerItem<ApplicationFunctionKeyHit>(x, x.Bounds)).ToArray());
            if (action.UiResult.Handled && action.Action.Kind == RoutedPointerActionKind.ItemPrimaryPressed)
                return new(new ApplicationFunctionKeyInteraction(frame, action.Action.Item!), action.UiResult);
        }

        if (frame.DirectoryShortcutBar is { } shortcuts)
        {
            var collection = new RoutedPointerCollection<ApplicationDirectoryShortcutHit>(new("application.directory-shortcut-bar"), shortcut => ApplicationTargetIds.DirectoryShortcut(shortcut.ShortcutNumber));
            RoutedPointerInput<ApplicationDirectoryShortcutHit> action = collection.RouteInput(input, context, shortcuts.Shortcuts.Select(x => new RoutedPointerItem<ApplicationDirectoryShortcutHit>(x, x.Bounds)).ToArray());
            if (action.UiResult.Handled && action.Action.Kind == RoutedPointerActionKind.ItemPrimaryPressed)
                return new(new ApplicationDirectoryShortcutInteraction(action.Action.Item!, frame.Keyboard.ActiveSide), action.UiResult);
        }
        return default;
    }

    private static bool TryRoutePanel(MouseConsoleInputEvent input, ApplicationPanelFrame? panel, UiInputRouteContext context, out RoutedApplicationPointerInput result)
    {
        result = default;
        if (panel is null) return false;
        var items = panel.VisibleItems.Select(item => new RoutedPointerItem<ApplicationPanelPointerTarget>(new(item), item.Bounds)).ToList();
        if (panel.RetryBounds is { } retryBounds) items.Add(new(new ApplicationPanelPointerTarget(IsRetry: true), retryBounds));
        if (panel.Side == PanelSide.Right)
            foreach (ApplicationPanelItemHit item in panel.VisibleItems.Where(item => item.Bounds.X == panel.Bounds.X + 1))
                items.Add(new(new ApplicationPanelPointerTarget(item), new Rect(panel.Bounds.X, item.Bounds.Y, 1, item.Bounds.Height)));
        var collection = new RoutedPointerCollection<ApplicationPanelPointerTarget>(ApplicationTargetIds.Panel(panel.Side), target => target.IsRetry ? ApplicationTargetIds.PanelRetry(panel.Side) : ApplicationTargetIds.PanelItem(panel.Side, target.Item!.ItemIndex));
        RoutedPointerInput<ApplicationPanelPointerTarget> action = collection.RouteInput(input, context, items);
        if (!action.UiResult.Handled) return false;
        result = new(new ApplicationPanelInteraction(panel.Side, panel, action.Action), action.UiResult);
        return true;
    }

    private static bool TryRouteScrollbar(MouseConsoleInputEvent input, ApplicationPanelFrame? panel, PanelSide side, RoutedScrollbarSurface surface, UiInputRouteContext context, out RoutedApplicationPointerInput result)
    {
        result = default;
        if (panel?.ScrollBar is not { } scrollbar) return false;
        RoutedScrollbarSurfaceInput routed = surface.RouteInput(input, scrollbar.VerticalScrollbarFrame, context);
        if (!routed.UiResult.Handled) return false;
        result = new(routed.FirstVisibleIndex is { } first ? new ApplicationPanelScrollInteraction(side, scrollbar.ViewportItems, first) : null, routed.UiResult);
        return true;
    }

    private readonly record struct RoutedApplicationPointerInput(ApplicationPointerInteraction? Interaction, UiInputResult UiResult);

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
