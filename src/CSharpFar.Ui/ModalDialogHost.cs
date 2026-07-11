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
        new(new ModalDialogLayerScope(_composition, _composition.PushOverlay(render)));

    public ModalDialogSession<TFrame> Open<TFrame>(Func<UiRenderContext, TFrame> render)
    {
        ArgumentNullException.ThrowIfNull(render);

        var committed = new UiCommittedState<TFrame>();
        var overlay = _composition.PushOverlay(context =>
        {
            TFrame frame = render(context);
            committed.Stage(context, frame);
        });

        return new ModalDialogSession<TFrame>(new ModalDialogLayerScope(_composition, overlay), committed);
    }
}

public sealed class ModalDialogSession : IDisposable
{
    private readonly ModalDialogLayerScope _scope;

    internal ModalDialogSession(ModalDialogLayerScope scope) => _scope = scope;

    public void Render() => _scope.Composition.Render();

    public ConsoleInputEvent ReadInput(CancellationToken cancellationToken = default)
        => _scope.Composition.ReadCompositionInput(cancellationToken);

    public bool TryReadInput(out ConsoleInputEvent? input)
        => _scope.Composition.TryReadCompositionInput(out input);

    public void Dispose() => _scope.Dispose();
}

public sealed class ModalDialogSession<TFrame> : IDisposable
{
    private readonly ModalDialogLayerScope _scope;
    private readonly UiCommittedState<TFrame> _committed;

    internal ModalDialogSession(ModalDialogLayerScope scope, UiCommittedState<TFrame> committed) =>
        (_scope, _committed) = (scope, committed);

    public TFrame Render()
    {
        _scope.Composition.Render();
        return _committed.Value;
    }

    public ConsoleInputEvent ReadInput(out TFrame frame, CancellationToken cancellationToken = default)
    {
        var input = _scope.Composition.ReadCompositionInput(cancellationToken);
        frame = _committed.Value;
        return input;
    }

    public bool TryReadInput(out ConsoleInputEvent? input, out TFrame frame)
    {
        bool result = _scope.Composition.TryReadCompositionInput(out input);
        frame = _committed.Value;
        return result;
    }

    public void Dispose() => _scope.Dispose();
}

internal sealed class ModalDialogLayerScope : IDisposable
{
    private UiCompositionHost? _composition;
    private IDisposable? _overlay;

    internal ModalDialogLayerScope(UiCompositionHost composition, IDisposable overlay) =>
        (_composition, _overlay) = (composition, overlay);

    public UiCompositionHost Composition => _composition ?? throw new ObjectDisposedException(nameof(ModalDialogSession));

    public void Dispose()
    {
        var composition = _composition;
        var overlay = _overlay;
        if (composition is null || overlay is null)
            return;

        overlay.Dispose();
        _overlay = null;
        _composition = null;
        composition.Render();
    }
}
