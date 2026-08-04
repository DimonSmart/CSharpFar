namespace CSharpFar.App;

internal sealed class PendingInvalidation<TPart>
    where TPart : struct, Enum
{
    private readonly ulong _full;
    private readonly Dictionary<ulong, long> _requestedAt = [];
    private long _generation;

    public PendingInvalidation(TPart full)
    {
        _full = ToBits(full);
        if (_full == 0 || (_full & (_full - 1)) != 0)
            throw new ArgumentException("Full invalidation must be one non-zero flag.", nameof(full));
    }

    public void Request(TPart parts)
    {
        ulong bits = ToBits(parts);
        if (bits == 0)
            return;

        long generation = ++_generation;
        foreach (ulong bit in EnumerateBits(bits))
            _requestedAt[bit] = generation;
    }

    public void RequestFull() => Request(FromBits(_full));

    public PendingInvalidationSnapshot<TPart> SnapshotForRenderAttempt()
    {
        ulong bits = _requestedAt.Keys.Aggregate(0UL, static (current, bit) => current | bit);
        if ((bits & _full) != 0)
            bits = _full;

        return new PendingInvalidationSnapshot<TPart>(FromBits(bits), _generation);
    }

    public void Commit(PendingInvalidationSnapshot<TPart> snapshot)
    {
        ulong snapshotBits = ToBits(snapshot.Parts);
        bool committedFull = (snapshotBits & _full) != 0;
        foreach ((ulong bit, long generation) in _requestedAt.ToArray())
        {
            if (generation <= snapshot.Generation && (committedFull || (snapshotBits & bit) != 0))
                _requestedAt.Remove(bit);
        }
    }

    private static IEnumerable<ulong> EnumerateBits(ulong bits)
    {
        while (bits != 0)
        {
            ulong bit = bits & (~bits + 1);
            yield return bit;
            bits &= ~bit;
        }
    }

    private static ulong ToBits(TPart value) => Convert.ToUInt64(value);
    private static TPart FromBits(ulong bits) => (TPart)Enum.ToObject(typeof(TPart), bits);
}

internal readonly record struct PendingInvalidationSnapshot<TPart>(TPart Parts, long Generation)
    where TPart : struct, Enum;
