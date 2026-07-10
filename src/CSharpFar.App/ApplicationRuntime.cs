using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Ui;

namespace CSharpFar.App;

internal sealed class ApplicationRuntime
{
    private readonly UiCompositionHost _composition;
    private readonly ApplicationRuntimeContext _context;

    public ApplicationRuntime(UiCompositionHost composition, ApplicationRuntimeContext context)
    {
        _composition = composition;
        _context = context;
    }

    public void Run()
    {
        try
        {
            _context.CaptureUnderlay();
            _context.StartWatchingInitialPanels();
            _composition.Render();

            while (_context.IsRunning())
            {
                ConsoleInputEvent evt;
                try
                {
                    evt = _composition.ReadInput(_context.WaitToken());
                }
                catch (OperationCanceledException)
                {
                    _context.ResetWaitToken();
                    _context.ProcessPendingRefreshes();
                    if (_context.IsRunning())
                        _composition.Render();
                    continue;
                }

                var renderRequest = DispatchInput(evt);
                if (!_context.IsRunning() || !renderRequest.ShouldRender)
                    continue;

                _composition.Render();
            }

            _composition.Screen.ClearScreen();
        }
        finally
        {
            _context.DisposeRuntimeState();
            _context.RestoreTerminal();
            _composition.Screen.SetCursorVisible(true);
        }
    }

    private ApplicationRuntimeRenderRequest DispatchInput(ConsoleInputEvent evt) =>
        evt switch
        {
            KeyConsoleInputEvent { Key: var key } => _context.HandleKeyInput(key),
            ModifierKeyConsoleInputEvent { Modifiers: var modifiers } => _context.HandleModifierInput(modifiers),
            MouseConsoleInputEvent mouseEvt => _context.HandleMouseInput(mouseEvt),
            _ => ApplicationRuntimeRenderRequest.None,
        };
}

internal sealed class ApplicationRuntimeContext
{
    public required Func<bool> IsRunning { get; init; }
    public required Func<CancellationToken> WaitToken { get; init; }
    public required Action CaptureUnderlay { get; init; }
    public required Action StartWatchingInitialPanels { get; init; }
    public required Action RestoreTerminal { get; init; }
    public required Action ResetWaitToken { get; init; }
    public required Action ProcessPendingRefreshes { get; init; }
    public required Action DisposeRuntimeState { get; init; }
    public required Func<ConsoleKeyInfo, ApplicationRuntimeRenderRequest> HandleKeyInput { get; init; }
    public required Func<ConsoleModifiers, ApplicationRuntimeRenderRequest> HandleModifierInput { get; init; }
    public required Func<MouseConsoleInputEvent, ApplicationRuntimeRenderRequest> HandleMouseInput { get; init; }
}

internal readonly record struct ApplicationRuntimeRenderRequest(bool ShouldRender, bool IsResize)
{
    public static ApplicationRuntimeRenderRequest None { get; } = new(false, false);
}
