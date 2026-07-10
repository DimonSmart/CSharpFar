using CSharpFar.Console;
using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public sealed class ModalDialogHost
{
    private readonly UiCompositionHost _composition;

    public ModalDialogHost(UiCompositionHost composition)
    {
        _composition = composition;
    }

    public ScreenRenderer Screen => _composition.Screen;

    public ModalDialogSession Open(Action<UiRenderContext> render) =>
        new(_composition, _composition.PushOverlay(render));
}

public sealed class ModalDialogSession : IDisposable
{
    private readonly UiCompositionHost _composition;
    private IDisposable? _overlay;

    internal ModalDialogSession(UiCompositionHost composition, IDisposable overlay) =>
        (_composition, _overlay) = (composition, overlay);

    public void Render() => _composition.Render();

    public ConsoleInputEvent ReadInput(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var input = _composition.Screen.ReadInput(cancellationToken);
            if (input is ConsoleResizeInputEvent || _composition.HasViewportChanged())
            {
                _composition.Render(isResizeRecovery: true);
                if (input is ConsoleResizeInputEvent)
                    continue;
            }
            return input;
        }
    }

    public bool TryReadInput(out ConsoleInputEvent? input)
    {
        while (_composition.Screen.TryReadInput(out input))
        {
            if (input is ConsoleResizeInputEvent || _composition.HasViewportChanged())
            {
                _composition.Render(isResizeRecovery: true);
                if (input is ConsoleResizeInputEvent)
                    continue;
            }
            return true;
        }

        input = null;
        return false;
    }

    public void Dispose()
    {
        var overlay = Interlocked.Exchange(ref _overlay, null);
        if (overlay is null)
            return;
        overlay.Dispose();
        _composition.Render();
    }
}
