namespace CSharpFar.FileSystem;

public sealed class CopyResumeAnalyzer
{
    private readonly CopyResumeOptions _options;

    public CopyResumeAnalyzer(CopyResumeOptions? options = null)
    {
        _options = options ?? CopyResumeOptions.Default;
    }

    public CopyResumePlan Analyze(
        string sourcePath,
        string destinationPath,
        CopyResumeSourceSnapshot? sourceSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        FileInfo sourceInfo;
        FileInfo destinationInfo;
        try
        {
            sourceInfo = new FileInfo(sourcePath);
            destinationInfo = new FileInfo(destinationPath);

            long sourceLength = sourceInfo.Length;
            long destinationLength = destinationInfo.Length;

            if (sourceSnapshot is not null &&
                (sourceSnapshot.Length != sourceLength ||
                 sourceSnapshot.LastWriteTimeUtc != sourceInfo.LastWriteTimeUtc))
            {
                return CopyResumePlan.CannotResume(
                    sourceLength,
                    destinationLength,
                    "Source file changed since the copy plan was built.");
            }

            if (destinationLength == sourceLength)
            {
                return CopyResumePlan.AlreadyComplete(
                    sourceLength,
                    destinationLength,
                    "Destination is already the same length as source.");
            }

            if (destinationLength > sourceLength)
            {
                return CopyResumePlan.CannotResume(
                    sourceLength,
                    destinationLength,
                    "Destination is larger than source.");
            }

            return AnalyzeRanges(sourcePath, destinationPath, sourceLength, destinationLength, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return CopyResumePlan.CannotResume(0, 0, ex.Message);
        }
    }

    private CopyResumePlan AnalyzeRanges(
        string sourcePath,
        string destinationPath,
        long sourceLength,
        long destinationLength,
        CancellationToken cancellationToken)
    {
        try
        {
            using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _options.ComparisonBufferBytes,
                FileOptions.RandomAccess);

            using var destination = new FileStream(
                destinationPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _options.ComparisonBufferBytes,
                FileOptions.RandomAccess);

            if (!source.CanSeek || !destination.CanSeek)
            {
                return CopyResumePlan.CannotResume(
                    sourceLength,
                    destinationLength,
                    "Source or destination stream cannot seek.");
            }

            if (destinationLength == 0)
                return CopyResumePlan.CanResume(sourceLength, destinationLength, safeResumeOffset: 0);

            long tailLength = Math.Min(_options.InitialTailValidationBytes, destinationLength);
            long tailOffset = destinationLength - tailLength;
            if (RangesMatch(source, destination, tailOffset, tailLength, cancellationToken))
            {
                return CopyResumePlan.CanResume(
                    sourceLength,
                    destinationLength,
                    safeResumeOffset: destinationLength);
            }

            for (long rollbackWindow = _options.InitialTailValidationBytes;
                 rollbackWindow <= _options.MaximumRollbackSearchBytes;
                 rollbackWindow = NextRollbackWindow(rollbackWindow))
            {
                cancellationToken.ThrowIfCancellationRequested();

                long candidateOffset = Math.Max(0, destinationLength - rollbackWindow);
                long validationLength = Math.Min(_options.InitialTailValidationBytes, destinationLength - candidateOffset);
                if (candidateOffset > 0 && validationLength < _options.MinimumValidationRangeBytes)
                    continue;

                if (RangesMatch(source, destination, candidateOffset, validationLength, cancellationToken))
                {
                    return CopyResumePlan.CanResume(
                        sourceLength,
                        destinationLength,
                        safeResumeOffset: candidateOffset);
                }

                if (rollbackWindow == _options.MaximumRollbackSearchBytes)
                    break;
            }

            return CopyResumePlan.CannotResume(
                sourceLength,
                destinationLength,
                "No matching validation range found within rollback limit.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return CopyResumePlan.CannotResume(sourceLength, destinationLength, ex.Message);
        }
    }

    private long NextRollbackWindow(long rollbackWindow)
    {
        long next = rollbackWindow * _options.RollbackMultiplier;
        return next > _options.MaximumRollbackSearchBytes
            ? _options.MaximumRollbackSearchBytes
            : next;
    }

    private bool RangesMatch(
        FileStream source,
        FileStream destination,
        long offset,
        long length,
        CancellationToken cancellationToken)
    {
        if (length == 0)
            return true;

        source.Position = offset;
        destination.Position = offset;

        byte[] sourceBuffer = new byte[_options.ComparisonBufferBytes];
        byte[] destinationBuffer = new byte[_options.ComparisonBufferBytes];
        long remaining = length;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int requested = (int)Math.Min(sourceBuffer.Length, remaining);
            int sourceRead = ReadExactlyUpTo(source, sourceBuffer, requested);
            int destinationRead = ReadExactlyUpTo(destination, destinationBuffer, requested);
            if (sourceRead != requested || destinationRead != requested)
                return false;

            if (!sourceBuffer.AsSpan(0, requested).SequenceEqual(destinationBuffer.AsSpan(0, requested)))
                return false;

            remaining -= requested;
        }

        return true;
    }

    private static int ReadExactlyUpTo(FileStream stream, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, total, count - total);
            if (read == 0)
                break;
            total += read;
        }

        return total;
    }
}
