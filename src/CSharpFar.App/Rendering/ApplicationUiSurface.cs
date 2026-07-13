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
    ApplicationSurfaceMode Mode);

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
            _coordinator.RenderHiddenCommandLineContent(context);
        else
            _coordinator.RenderMainContent(context);

        return new ApplicationUiFrame(
            context.Viewport,
            _hidden
                ? ApplicationSurfaceMode.HiddenCommandLine
                : ApplicationSurfaceMode.Panels);
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
        return UiInputResult.HandledResult;
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
