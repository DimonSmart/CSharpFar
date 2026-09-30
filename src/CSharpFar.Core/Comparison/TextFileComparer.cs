namespace CSharpFar.Core.Comparison;

public sealed class TextFileComparer : IFileComparer
{
    private const int CharacterCompareBufferSize = 4096;

    private readonly ComparisonOptions _options;
    private readonly IComparisonFileSystem _fileSystem;
    private readonly ByteContentFileComparer _byteComparer;
    private readonly int _byteBufferSize;
    private readonly int _detectionPrefixSize;

    public TextFileComparer(ComparisonOptions options, IComparisonFileSystem? fileSystem = null)
        : this(
            options,
            fileSystem ?? new LocalComparisonFileSystem(),
            TextCanonicalReader.DefaultByteBufferSize,
            TextCanonicalReader.DefaultDetectionPrefixSize)
    {
    }

    internal TextFileComparer(
        ComparisonOptions options,
        IComparisonFileSystem fileSystem,
        int byteBufferSize,
        int detectionPrefixSize)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _options = options;
        _fileSystem = fileSystem;
        _byteComparer = new ByteContentFileComparer(fileSystem);
        _byteBufferSize = byteBufferSize;
        _detectionPrefixSize = detectionPrefixSize;
    }

    public FileCompareOutcome Compare(
        FileEntry left,
        FileEntry right,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var leftReader = CreateReader(left, cancellationToken);
        using var rightReader = CreateReader(right, cancellationToken);

        if (leftReader.Detection.IsBinary || rightReader.Detection.IsBinary)
            return _byteComparer.Compare(left, right, cancellationToken);

        if (_options.TextBom == TextBomComparison.Exact &&
            leftReader.Detection.HasByteOrderMark != rightReader.Detection.HasByteOrderMark)
        {
            return new FileCompareOutcome(false, "BOM presence differs.", leftReader.SourceBytesRead);
        }

        if (_options.TextBom is not TextBomComparison.Exact and not TextBomComparison.Ignore)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_options.TextBom), _options.TextBom, "Unsupported BOM comparison mode.");
        }

        char[] leftBuffer = new char[CharacterCompareBufferSize];
        char[] rightBuffer = new char[CharacterCompareBufferSize];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int leftRead = leftReader.Read(leftBuffer, cancellationToken);
            int rightRead = rightReader.Read(rightBuffer, cancellationToken);

            if (leftRead != rightRead)
                return new FileCompareOutcome(false, "Text length differs.", leftReader.SourceBytesRead);
            if (leftRead == 0)
                return new FileCompareOutcome(true, ComparedBytes: leftReader.SourceBytesRead);
            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
                return new FileCompareOutcome(false, "Text differs.", leftReader.SourceBytesRead);
        }
    }

    private TextCanonicalReader CreateReader(FileEntry entry, CancellationToken cancellationToken) =>
        new(
            _fileSystem.OpenRead(entry.FullPath),
            _options,
            cancellationToken,
            _byteBufferSize,
            _detectionPrefixSize);
}
