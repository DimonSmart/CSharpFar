namespace CSharpFar.Ui;

internal sealed class PendingInputSlot<TPacket> where TPacket : class
{
    private TPacket? _packet;

    public void Store(TPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (_packet is not null)
            throw new InvalidOperationException("Input was dispatched before the previous packet was consumed.");

        _packet = packet;
    }

    public bool TryTake(out TPacket packet)
    {
        if (_packet is null)
        {
            packet = null!;
            return false;
        }

        packet = _packet;
        _packet = null;
        return true;
    }

    public void Clear() => _packet = null;
}
