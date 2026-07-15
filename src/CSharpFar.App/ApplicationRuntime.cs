using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.App.Rendering;
using CSharpFar.Core.Menu;
using CSharpFar.Ui;

namespace CSharpFar.App;

internal sealed class ApplicationRuntime
{
    private readonly UiCompositionHost _composition;
    private readonly ApplicationUiSurface _applicationSurface;
    private readonly IDisposable _applicationUiLayers;
    private readonly ApplicationRuntimeContext _context;

    public ApplicationRuntime(
        UiCompositionHost composition,
        ApplicationUiSurface applicationSurface,
        ApplicationRuntimeContext context)
        : this(composition, applicationSurface, NullDisposable.Instance, context)
    {
    }

    public ApplicationRuntime(
        UiCompositionHost composition,
        ApplicationUiSurface applicationSurface,
        IDisposable applicationUiLayers,
        ApplicationRuntimeContext context)
    {
        _composition = composition;
        _applicationSurface = applicationSurface;
        _applicationUiLayers = applicationUiLayers;
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

                UiInputResult routed = _composition.DispatchInput(evt);
                ApplicationRuntimeRenderRequest menuCommandRequest = ApplicationRuntimeRenderRequest.None;
                if (_context.TryTakeMenuCommand(out var menuCommand))
                    menuCommandRequest = _context.ExecuteMenuCommand(menuCommand);

                ApplicationRuntimeRenderRequest applicationRequest = ApplicationRuntimeRenderRequest.None;

                if (_applicationSurface.TryTakeInput(out var applicationInput))
                    applicationRequest = DispatchApplicationInput(applicationInput.Input);

                bool shouldRender =
                    routed.Invalidate ||
                    menuCommandRequest.ShouldRender ||
                    applicationRequest.ShouldRender;
                if (!_context.IsRunning() || !shouldRender)
                    continue;

                _composition.Render();
            }

            _composition.Screen.ClearScreen();
        }
        finally
        {
            _context.DisposeRuntimeState();
            _applicationUiLayers.Dispose();
            _context.RestoreTerminal();
            _composition.Screen.SetCursorVisible(true);
        }
    }

    private ApplicationRuntimeRenderRequest DispatchApplicationInput(ConsoleInputEvent input) =>
        input switch
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
    public TryTakeMenuCommand TryTakeMenuCommand { get; init; } = static (out MenuCommandRequest request) =>
    {
        request = null!;
        return false;
    };

    public Func<MenuCommandRequest, ApplicationRuntimeRenderRequest> ExecuteMenuCommand { get; init; } =
        _ => ApplicationRuntimeRenderRequest.None;
}

internal delegate bool TryTakeMenuCommand(out MenuCommandRequest request);

internal sealed class NullDisposable : IDisposable
{
    public static NullDisposable Instance { get; } = new();

    private NullDisposable()
    {
    }

    public void Dispose()
    {
    }
}

internal readonly record struct ApplicationRuntimeRenderRequest(bool ShouldRender)
{
    public static ApplicationRuntimeRenderRequest None { get; } = new(false);
}
