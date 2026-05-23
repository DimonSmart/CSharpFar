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

        FileInfo sourceInfo = new(sourcePath);
        long sourceLength;
        try
        {
            sourceLength = sourceInfo.Length;
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            return CopyResumePlan.CannotResume(0, 0, ex.Message, CopyResumeReadFailureSide.Source);
        }

        FileInfo destinationInfo = new(destinationPath);
        long destinationLength;
        try
        {
            destinationLength = destinationInfo.Length;
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            return CopyResumePlan.CannotResume(sourceLength, 0, ex.Message, CopyResumeReadFailureSide.Destination);
        }

        DateTime sourceLastWriteTimeUtc;
        try
        {
            sourceLastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            return CopyResumePlan.CannotResume(sourceLength, destinationLength, ex.Message, CopyResumeReadFailureSide.Source);
        }

        if (sourceSnapshot is not null &&
            (sourceSnapshot.Length != sourceLength ||
             sourceSnapshot.LastWriteTimeUtc != sourceLastWriteTimeUtc))
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

            if (!source.CanSeek)
            {
                return CopyResumePlan.CannotResume(
                    sourceLength,
                    destinationLength,
                    "Source stream cannot seek.");
            }

            return AnalyzeRangesWithSource(source, destinationPath, sourceLength, destinationLength, cancellationToken);
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            return CopyResumePlan.CannotResume(
                sourceLength,
                destinationLength,
                ex.Message,
                CopyResumeReadFailureSide.Source);
        }
    }

    private CopyResumePlan AnalyzeRangesWithSource(
        FileStream source,
        string destinationPath,
        long sourceLength,
        long destinationLength,
        CancellationToken cancellationToken)
    {
        try
        {
            using var destination = new FileStream(
                destinationPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _options.ComparisonBufferBytes,
                FileOptions.RandomAccess);

            if (!destination.CanSeek)
            {
                return CopyResumePlan.CannotResume(
                    sourceLength,
                    destinationLength,
                    "Destination stream cannot seek.");
            }

            if (destinationLength == 0)
                return CopyResumePlan.CanResume(sourceLength, destinationLength, safeResumeOffset: 0);

            long tailLength = Math.Min(_options.InitialTailValidationBytes, destinationLength);
            long tailOffset = destinationLength - tailLength;
            RangeMatchResult tailResult = RangesMatch(source, destination, tailOffset, tailLength, cancellationToken);
            if (tailResult.ReadFailureSide is { } tailReadFailureSide)
            {
                return CopyResumePlan.CannotResume(
                    sourceLength,
                    destinationLength,
                    tailResult.ReadFailureMessage,
                    tailReadFailureSide);
            }

            if (tailResult.IsMatch)
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

                RangeMatchResult candidateResult = RangesMatch(source, destination, candidateOffset, validationLength, cancellationToken);
                if (candidateResult.ReadFailureSide is { } candidateReadFailureSide)
                {
                    return CopyResumePlan.CannotResume(
                        sourceLength,
                        destinationLength,
                        candidateResult.ReadFailureMessage,
                        candidateReadFailureSide);
                }

                if (candidateResult.IsMatch)
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
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            return CopyResumePlan.CannotResume(
                sourceLength,
                destinationLength,
                ex.Message,
                CopyResumeReadFailureSide.Destination);
        }
    }

    private long NextRollbackWindow(long rollbackWindow)
    {
        long next = rollbackWindow * _options.RollbackMultiplier;
        return next > _options.MaximumRollbackSearchBytes
            ? _options.MaximumRollbackSearchBytes
            : next;
    }

    private RangeMatchResult RangesMatch(
        FileStream source,
        FileStream destination,
        long offset,
        long length,
        CancellationToken cancellationToken)
    {
        if (length == 0)
            return RangeMatchResult.Match;

        try
        {
            source.Position = offset;
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            return RangeMatchResult.ReadFailure(CopyResumeReadFailureSide.Source, ex.Message);
        }

        try
        {
            destination.Position = offset;
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            return RangeMatchResult.ReadFailure(CopyResumeReadFailureSide.Destination, ex.Message);
        }

        byte[] sourceBuffer = new byte[_options.ComparisonBufferBytes];
        byte[] destinationBuffer = new byte[_options.ComparisonBufferBytes];
        long remaining = length;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int requested = (int)Math.Min(sourceBuffer.Length, remaining);
            if (!TryReadExactlyUpTo(
                source,
                sourceBuffer,
                requested,
                out int sourceRead,
                out string sourceReadError))
            {
                return RangeMatchResult.ReadFailure(CopyResumeReadFailureSide.Source, sourceReadError);
            }

            if (!TryReadExactlyUpTo(
                destination,
                destinationBuffer,
                requested,
                out int destinationRead,
                out string destinationReadError))
            {
                return RangeMatchResult.ReadFailure(CopyResumeReadFailureSide.Destination, destinationReadError);
            }

            if (sourceRead != requested || destinationRead != requested)
                return RangeMatchResult.Mismatch;

            if (!sourceBuffer.AsSpan(0, requested).SequenceEqual(destinationBuffer.AsSpan(0, requested)))
                return RangeMatchResult.Mismatch;

            remaining -= requested;
        }

        return RangeMatchResult.Match;
    }

    private static bool TryReadExactlyUpTo(
        FileStream stream,
        byte[] buffer,
        int count,
        out int total,
        out string error)
    {
        total = 0;
        error = string.Empty;

        try
        {
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read == 0)
                    break;
                total += read;
            }

            return true;
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsFileAccessException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private readonly record struct RangeMatchResult(
        bool IsMatch,
        CopyResumeReadFailureSide? ReadFailureSide,
        string ReadFailureMessage)
    {
        public static RangeMatchResult Match { get; } = new(true, null, string.Empty);
        public static RangeMatchResult Mismatch { get; } = new(false, null, string.Empty);

        public static RangeMatchResult ReadFailure(
            CopyResumeReadFailureSide readFailureSide,
            string readFailureMessage) =>
            new(false, readFailureSide, readFailureMessage);
    }
}
