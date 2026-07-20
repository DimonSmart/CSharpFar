using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

internal delegate bool TryTakeCompositionPacket<TPacket>(out TPacket packet);

internal readonly record struct CompositionInputPumpResult<TPacket>(TPacket? Packet, bool IsWake)
{
    public TPacket RequiredPacket => !IsWake && Packet is { } packet
        ? packet
        : throw new InvalidOperationException("The composition pump result does not contain an input packet.");

    public static CompositionInputPumpResult<TPacket> Input(TPacket packet) => new(packet, IsWake: false);

    public static CompositionInputPumpResult<TPacket> Wake() => new(default, IsWake: true);
}

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

    public CompositionInputPumpResult<TPacket> ReadOrWake(
        Func<DateTimeOffset?> getNextWakeUtc,
        CancellationToken cancellationToken = default,
        CancellationToken wakeSignal = default)
    {
        ArgumentNullException.ThrowIfNull(getNextWakeUtc);

        _ensureActive();
        if (_tryTakePacket(out var pending))
            return CompositionInputPumpResult<TPacket>.Input(pending);

        while (true)
        {
            _ensureActive();
            var read = _composition.ReadCompositionInputUntil(getNextWakeUtc(), cancellationToken, wakeSignal);
            if (read.IsWake)
                return CompositionInputPumpResult<TPacket>.Wake();

            UiInputResult dispatch = _composition.DispatchInput(read.Input);
            if (_tryTakePacket(out var packet))
                return CompositionInputPumpResult<TPacket>.Input(packet);

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
