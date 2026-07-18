using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

internal delegate bool TryTakeCompositionPacket<TPacket>(out TPacket packet);

internal sealed class CompositionInputPump<TPacket>
{
    private readonly UiCompositionHost _composition;
    private readonly TryTakeCompositionPacket<TPacket> _tryTakePacket;
    private readonly Action _ensureActive;

    public CompositionInputPump(
        UiCompositionHost composition,
        TryTakeCompositionPacket<TPacket> tryTakePacket,
        Action? ensureActive = null)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _tryTakePacket = tryTakePacket ?? throw new ArgumentNullException(nameof(tryTakePacket));
        _ensureActive = ensureActive ?? NoOp;
    }

    public TPacket Read(CancellationToken cancellationToken = default)
    {
        _ensureActive();
        if (_tryTakePacket(out var pending))
            return pending;

        while (true)
        {
            _ensureActive();
            ConsoleInputEvent semanticInput = _composition.ReadCompositionInput(cancellationToken);
            UiInputResult dispatch = _composition.DispatchInput(semanticInput);
            if (_tryTakePacket(out var packet))
                return packet;

            if (dispatch.Invalidate)
                _composition.Render();
        }
    }

    public bool TryRead(out TPacket packet)
    {
        _ensureActive();
        if (_tryTakePacket(out packet))
            return true;

        while (true)
        {
            _ensureActive();
            if (!_composition.TryReadCompositionInput(out ConsoleInputEvent? semanticInput))
            {
                packet = default!;
                return false;
            }

            if (semanticInput is null)
                continue;

            UiInputResult dispatch = _composition.DispatchInput(semanticInput);
            if (_tryTakePacket(out packet))
                return true;

            if (dispatch.Invalidate)
                _composition.Render();
        }
    }

    private static void NoOp()
    {
    }
}
