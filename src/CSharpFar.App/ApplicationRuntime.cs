using CSharpFar.Console;
using CSharpFar.Console.Input;

namespace CSharpFar.App;

internal sealed class ApplicationRuntime
{
    private readonly ScreenRenderer _screen;
    private readonly ApplicationRuntimeContext _context;

    public ApplicationRuntime(ScreenRenderer screen, ApplicationRuntimeContext context)
    {
        _screen = screen;
        _context = context;
    }

    public void Run()
    {
        try
        {
            _context.CaptureUnderlay();
            _context.StartWatchingInitialPanels();
            _context.RenderUntilStable();

            while (_context.IsRunning())
            {
                ConsoleInputEvent evt;
                try
                {
                    evt = _screen.ReadInput(_context.WaitToken());
                }
                catch (OperationCanceledException)
                {
                    _context.ResetWaitToken();
                    _context.ProcessPendingRefreshes();
                    if (_context.IsRunning() && _context.HasVisiblePanels())
                        _context.RenderUntilStable();
                    continue;
                }

                var renderRequest = DispatchInput(evt);
                if (!renderRequest.ShouldRender)
                    renderRequest = _context.CheckViewportAfterInput();

                if (!_context.IsRunning() || !renderRequest.ShouldRender)
                    continue;

                if (_context.HasVisiblePanels())
                {
                    _context.RenderUntilStable();
                }
                else
                {
                    _context.RenderCommandLineOnlyUntilStable(renderRequest.IsResize);
                }
            }

            _screen.ClearScreen();
        }
        finally
        {
            _context.DisposeRuntimeState();
            _context.RestoreTerminal();
            _screen.SetCursorVisible(true);
        }
    }

    private ApplicationRuntimeRenderRequest DispatchInput(ConsoleInputEvent evt) =>
        evt switch
        {
            ConsoleResizeInputEvent => _context.HandleResizeInput(),
            KeyConsoleInputEvent { Key: var key } => _context.HandleKeyInput(key),
            ModifierKeyConsoleInputEvent { Modifiers: var modifiers } => _context.HandleModifierInput(modifiers),
            MouseConsoleInputEvent mouseEvt => _context.HandleMouseInput(mouseEvt),
            _ => ApplicationRuntimeRenderRequest.None,
        };
}

internal sealed class ApplicationRuntimeContext
{
    public required Func<bool> IsRunning { get; init; }
    public required Func<bool> HasVisiblePanels { get; init; }
    public required Func<CancellationToken> WaitToken { get; init; }
    public required Action CaptureUnderlay { get; init; }
    public required Action StartWatchingInitialPanels { get; init; }
    public required Action RenderUntilStable { get; init; }
    public required Action<bool> RenderCommandLineOnlyUntilStable { get; init; }
    public required Action RestoreTerminal { get; init; }
    public required Action ResetWaitToken { get; init; }
    public required Action ProcessPendingRefreshes { get; init; }
    public required Action DisposeRuntimeState { get; init; }
    public required Func<ApplicationRuntimeRenderRequest> HandleResizeInput { get; init; }
    public required Func<ApplicationRuntimeRenderRequest> CheckViewportAfterInput { get; init; }
    public required Func<ConsoleKeyInfo, ApplicationRuntimeRenderRequest> HandleKeyInput { get; init; }
    public required Func<ConsoleModifiers, ApplicationRuntimeRenderRequest> HandleModifierInput { get; init; }
    public required Func<MouseConsoleInputEvent, ApplicationRuntimeRenderRequest> HandleMouseInput { get; init; }
}

internal readonly record struct ApplicationRuntimeRenderRequest(bool ShouldRender, bool IsResize)
{
    public static ApplicationRuntimeRenderRequest None { get; } = new(false, false);
}
