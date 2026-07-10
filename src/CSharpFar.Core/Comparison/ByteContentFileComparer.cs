namespace CSharpFar.Core.Comparison;

public sealed class ByteContentFileComparer : IFileComparer
{
    private const int BufferSize = 1024 * 128;
    private readonly IComparisonFileSystem _fileSystem;

    public ByteContentFileComparer(IComparisonFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem ?? new LocalComparisonFileSystem();
    }

    public FileCompareOutcome Compare(FileEntry left, FileEntry right, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (left.Size != right.Size)
            return new FileCompareOutcome(false, "Size differs.");

        byte[] leftBuffer = new byte[BufferSize];
        byte[] rightBuffer = new byte[BufferSize];
        long comparedBytes = 0;

        using Stream leftStream = _fileSystem.OpenRead(left.FullPath);
        using Stream rightStream = _fileSystem.OpenRead(right.FullPath);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length);
            int rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);
            comparedBytes += leftRead;

            if (leftRead != rightRead)
                return new FileCompareOutcome(false, "Content length changed while reading.", comparedBytes);
            if (leftRead == 0)
                return new FileCompareOutcome(true, ComparedBytes: comparedBytes);

            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
                return new FileCompareOutcome(false, "Content differs.", comparedBytes);
        }
    }
}
