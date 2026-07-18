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
    ApplicationDirectoryShortcutBarFrame? DirectoryShortcutBar);

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
    public static UiTargetId FunctionKeyBar { get; } = new("application.function-key-bar");
    public static UiTargetId DirectoryShortcutBar { get; } = new("application.directory-shortcut-bar");

    public static UiTargetId Panel(PanelSide side) =>
        side == PanelSide.Left ? LeftPanel : RightPanel;

    public static UiTargetId PanelScrollbar(PanelSide side) =>
        side == PanelSide.Left ? LeftPanelScrollbar : RightPanelScrollbar;
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
    int FirstVisibleIndex)
{
    public ScrollState ToScrollState() => new()
    {
        TotalItems = TotalItems,
        ViewportItems = ViewportItems,
        FirstVisibleIndex = FirstVisibleIndex,
    };
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
    private bool _hidden;
    private UiRoutedInput<ApplicationUiFrame>? _pendingInput;

    public ApplicationUiSurface(ApplicationRenderContext context, ApplicationRenderCoordinator coordinator)
    {
        _context = context;
        _coordinator = coordinator;
    }

    public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;

    public IDisposable BeginFrame(UiRenderRequest request)
    {
        _hidden = _context.App.WorkspaceMode == ApplicationWorkspaceMode.HiddenCommandLine;
        _context.TerminalSurface.ApplyMode();
        _context.Screen.SetRenderingOutputMode(true);

        if (!_hidden)
            return _context.Screen.BeginFrame();

        if (request.IsResizeRecovery)
        {
            if (_context.TerminalSurface.UsesTerminalScreenMode)
                _context.TerminalSurface.PrepareHiddenResize();
            else
                _context.TerminalSurface.RestoreHiddenScreen();
        }

        var viewport = _context.Screen.GetViewport();
        var row = ApplicationLayoutService.CommandLineRow(viewport.Size);
        _context.TerminalSurface.PrepareHiddenCommandLineOverlay(viewport, row, viewport.Width);
        return _context.TerminalSurface.UsesTerminalScreenMode
            ? _context.Screen.BeginFrameFromCurrentViewportCapture()
            : _context.Screen.BeginFrame();
    }

    protected override ApplicationUiFrame RenderFrame(UiRenderContext context)
    {
        if (_hidden)
            return _coordinator.RenderHiddenCommandLineContent(context);
        else
            return _coordinator.RenderMainContent(context);
    }

    public void CompleteFrame(UiFrameCompletion completion)
    {
        if (_hidden && completion.WasCommitted)
            _context.TerminalSurface.MarkHiddenCommandLineRenderCompleted();
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

        if (_pendingInput is not null)
            throw new InvalidOperationException("Application input was dispatched before the previous input was processed.");

        _pendingInput = new UiRoutedInput<ApplicationUiFrame>(input, frame, context.Target, context.RouteKind);
        if (input is MouseConsoleInputEvent { Button: MouseButton.Left, Kind: MouseEventKind.Down } down &&
            context.RouteKind == UiInputRouteKind.HitTarget &&
            TryGetScrollbarFrame(frame, context.Target, out var scrollbar) &&
            ScrollBarInteraction.HitTest(scrollbar.Bounds, scrollbar.ToScrollState(), down.X, down.Y).Part == ScrollBarHitPart.Thumb)
        {
            return UiInputResult.CaptureMouse(context.Target!, MouseButton.Left);
        }

        if (input is MouseConsoleInputEvent { Button: MouseButton.Left, Kind: MouseEventKind.Up } &&
            context.RouteKind == UiInputRouteKind.CapturedTarget &&
            IsScrollbarTarget(context.Target))
        {
            return UiInputResult.ReleaseMouse();
        }

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
        var focusEntries = new List<UiFocusEntry>
        {
            new(ApplicationTargetIds.CommandLine, 0, IsEnabled: true, frame.CommandLine.Cursor),
        };
        if (frame.Mode == ApplicationWorkspaceMode.Panels)
        {
            if (frame.LeftPanel is not null)
                focusEntries.Add(new UiFocusEntry(ApplicationTargetIds.LeftPanel, 1));
            if (frame.RightPanel is not null)
                focusEntries.Add(new UiFocusEntry(ApplicationTargetIds.RightPanel, 2));
        }

        var focus = new UiFocusFrame(focusEntries, ApplicationTargetIds.CommandLine);

        var hitRegions = new List<UiHitRegion>();
        if (IsVisible(frame.CommandLine.Bounds, frame.Viewport))
            hitRegions.Add(new UiHitRegion(ApplicationTargetIds.CommandLine, frame.CommandLine.Bounds));

        if (frame.Mode == ApplicationWorkspaceMode.Panels)
        {
            AddPanelRegions(hitRegions, frame.LeftPanel, frame.Viewport);
            AddPanelRegions(hitRegions, frame.RightPanel, frame.Viewport);

            if (frame.FunctionKeyBar is { } functionKeyBar)
            {
                foreach (var action in functionKeyBar.Actions)
                    hitRegions.Add(new UiHitRegion(ApplicationTargetIds.FunctionKeyBar, action.Bounds));
            }

            if (frame.DirectoryShortcutBar is { } shortcutBar)
            {
                foreach (var shortcut in shortcutBar.Shortcuts)
                    hitRegions.Add(new UiHitRegion(ApplicationTargetIds.DirectoryShortcutBar, shortcut.Bounds));
            }
        }

        return new UiInteractionFrame(hitRegions, focus, ApplicationTargetIds.WorkspaceKeyboard);
    }

    protected override void OnFrameCommitted(ApplicationUiFrame frame)
    {
        RebaseScrollbarDrag(frame);
    }

    private void RebaseScrollbarDrag(ApplicationUiFrame frame)
    {
        if (_context.Ui.PanelScrollbarDrag is not { } drag)
            return;

        var scrollbar = drag.Side == PanelSide.Left
            ? frame.LeftPanel?.ScrollBar
            : frame.RightPanel?.ScrollBar;
        if (scrollbar is null ||
            !ScrollBarInteraction.IsInteractive(scrollbar.Bounds, scrollbar.ToScrollState()))
        {
            _context.Ui.PanelScrollbarDrag = null;
            return;
        }

        var rebased = ScrollBarInteraction.RebaseDrag(
            drag.DragState,
            scrollbar.Bounds,
            scrollbar.TotalItems,
            scrollbar.ViewportItems);
        _context.Ui.PanelScrollbarDrag = rebased.HasValue
            ? new PanelScrollbarDrag(drag.Side, rebased.Value)
            : null;
    }

    private static void AddPanelRegions(
        List<UiHitRegion> hitRegions,
        ApplicationPanelFrame? panel,
        ConsoleViewport viewport)
    {
        if (panel is null)
            return;

        if (IsVisible(panel.Bounds, viewport))
            hitRegions.Add(new UiHitRegion(ApplicationTargetIds.Panel(panel.Side), panel.Bounds));
        if (panel.ScrollBar is { } scrollbar &&
            IsVisible(scrollbar.Bounds, viewport) &&
            ScrollBarInteraction.IsInteractive(scrollbar.Bounds, scrollbar.ToScrollState()))
        {
            hitRegions.Add(new UiHitRegion(ApplicationTargetIds.PanelScrollbar(panel.Side), scrollbar.Bounds));
        }
    }

    private static bool IsVisible(Rect bounds, ConsoleViewport viewport) =>
        bounds.Width > 0 &&
        bounds.Height > 0 &&
        bounds.Right > 0 &&
        bounds.Bottom > 0 &&
        bounds.X < viewport.Width &&
        bounds.Y < viewport.Height;

    private static bool TryGetScrollbarFrame(
        ApplicationUiFrame frame,
        UiTargetId? target,
        out ApplicationScrollBarFrame scrollbar)
    {
        if (target == ApplicationTargetIds.LeftPanelScrollbar && frame.LeftPanel?.ScrollBar is { } left)
        {
            scrollbar = left;
            return true;
        }

        if (target == ApplicationTargetIds.RightPanelScrollbar && frame.RightPanel?.ScrollBar is { } right)
        {
            scrollbar = right;
            return true;
        }

        scrollbar = null!;
        return false;
    }

    private static bool IsScrollbarTarget(UiTargetId? target) =>
        target == ApplicationTargetIds.LeftPanelScrollbar ||
        target == ApplicationTargetIds.RightPanelScrollbar;

    internal bool TryTakeInput(out UiRoutedInput<ApplicationUiFrame> routed)
    {
        if (_pendingInput is null)
        {
            routed = null!;
            return false;
        }

        routed = _pendingInput;
        _pendingInput = null;
        return true;
    }
}
