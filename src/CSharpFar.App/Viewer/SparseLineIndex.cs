namespace CSharpFar.App.Viewer;

internal readonly record struct SparseLineCheckpoint(long LineNumber, long ByteOffset);

internal sealed class SparseLineIndex
{
    public const int DefaultLineInterval = 10_000;
    public const long DefaultByteInterval = 4L * 1024 * 1024;

    private readonly int _lineInterval;
    private readonly long _byteInterval;
    private readonly List<SparseLineCheckpoint> _checkpoints = [];

    public SparseLineIndex(
        int lineInterval = DefaultLineInterval,
        long byteInterval = DefaultByteInterval)
    {
        if (lineInterval <= 0)
            throw new ArgumentOutOfRangeException(nameof(lineInterval));
        if (byteInterval <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteInterval));

        _lineInterval = lineInterval;
        _byteInterval = byteInterval;
    }

    public IReadOnlyList<SparseLineCheckpoint> Checkpoints => _checkpoints;

    public void Add(long lineNumber, long byteOffset)
    {
        if (lineNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        if (byteOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(byteOffset));

        if (_checkpoints.Count == 0)
        {
            _checkpoints.Add(new SparseLineCheckpoint(lineNumber, byteOffset));
            return;
        }

        var last = _checkpoints[^1];
        if (lineNumber <= last.LineNumber)
            return;

        bool lineIntervalReached = lineNumber - last.LineNumber >= _lineInterval;
        bool byteIntervalReached = byteOffset - last.ByteOffset >= _byteInterval;
        if (lineIntervalReached || byteIntervalReached)
            _checkpoints.Add(new SparseLineCheckpoint(lineNumber, byteOffset));
    }

    public SparseLineCheckpoint FindNearestLine(long lineNumber)
    {
        if (lineNumber <= 1 || _checkpoints.Count == 0)
            return new SparseLineCheckpoint(1, 0);

        int index = _checkpoints.BinarySearch(
            new SparseLineCheckpoint(lineNumber, long.MaxValue),
            LineNumberComparer.Instance);

        if (index >= 0)
            return _checkpoints[index];

        int previousIndex = Math.Max(0, ~index - 1);
        return _checkpoints[previousIndex];
    }

    public SparseLineCheckpoint FindNearestOffset(long byteOffset)
    {
        if (byteOffset <= 0 || _checkpoints.Count == 0)
            return new SparseLineCheckpoint(1, 0);

        SparseLineCheckpoint best = _checkpoints[0];
        foreach (var checkpoint in _checkpoints)
        {
            if (checkpoint.ByteOffset > byteOffset)
                break;

            best = checkpoint;
        }

        return best;
    }

    private sealed class LineNumberComparer : IComparer<SparseLineCheckpoint>
    {
        public static readonly LineNumberComparer Instance = new();

        public int Compare(SparseLineCheckpoint x, SparseLineCheckpoint y) =>
            x.LineNumber.CompareTo(y.LineNumber);
    }
}
