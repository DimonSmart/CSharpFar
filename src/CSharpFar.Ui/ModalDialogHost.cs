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

    public UiCompositionHost Composition => _composition;

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
        => _composition.ReadCompositionInput(cancellationToken);

    public bool TryReadInput(out ConsoleInputEvent? input)
        => _composition.TryReadCompositionInput(out input);

    public void Dispose()
    {
        var overlay = _overlay;
        if (overlay is null)
            return;
        overlay.Dispose();
        _overlay = null;
        _composition.Render();
    }
}
