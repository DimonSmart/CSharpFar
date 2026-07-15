using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal enum ApplicationSurfaceMode
{
    Panels,
    HiddenCommandLine,
}

internal sealed record ApplicationUiFrame(
    ConsoleViewport Viewport,
    ApplicationSurfaceMode Mode,
    ApplicationCommandLineFrame CommandLine,
    Rect? LeftPanelBounds,
    Rect? RightPanelBounds);

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
    public static UiTargetId CommandLine { get; } = new("application.command-line");
}

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
        _hidden = !_context.HasVisiblePanels();
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
        var focus = new UiFocusFrame(
            [new UiFocusEntry(ApplicationTargetIds.CommandLine, 0, IsEnabled: true, frame.CommandLine.Cursor)],
            ApplicationTargetIds.CommandLine);

        if (frame.CommandLine.Bounds.Width <= 0 ||
            frame.CommandLine.Bounds.Height <= 0 ||
            frame.CommandLine.Bounds.Y < 0 ||
            frame.CommandLine.Bounds.Y >= frame.Viewport.Height)
        {
            return new UiInteractionFrame([], focus);
        }

        return new UiInteractionFrame(
            [new UiHitRegion(ApplicationTargetIds.CommandLine, frame.CommandLine.Bounds)],
            focus);
    }

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
