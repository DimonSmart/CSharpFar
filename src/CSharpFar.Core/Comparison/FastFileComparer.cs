namespace CSharpFar.Core.Comparison;

public sealed class FastFileComparer : IFileComparer
{
    private readonly TimeSpan _timestampTolerance;

    public FastFileComparer(TimeSpan timestampTolerance)
    {
        _timestampTolerance = timestampTolerance;
    }

    public FileCompareOutcome Compare(FileEntry left, FileEntry right, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (left.Size != right.Size)
            return new FileCompareOutcome(false, "Size differs.");

        if (left.LastWriteTimeUtc is null || right.LastWriteTimeUtc is null)
            return new FileCompareOutcome(false, "Modified time is unavailable.");

        var delta = (left.LastWriteTimeUtc.Value - right.LastWriteTimeUtc.Value).Duration();
        return delta <= _timestampTolerance
            ? new FileCompareOutcome(true)
            : new FileCompareOutcome(false, "Modified time differs.");
    }
}
