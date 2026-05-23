namespace CSharpFar.FileSystem;

public sealed record CopyResumePlan
{
    public required CopyResumePlanKind Kind { get; init; }
    public required long SourceLength { get; init; }
    public required long DestinationLength { get; init; }
    public long SafeResumeOffset { get; init; }
    public long RollbackBytes => Math.Max(0, DestinationLength - SafeResumeOffset);
    public string Reason { get; init; } = string.Empty;
    public CopyResumeReadFailureSide? ReadFailureSide { get; init; }

    public static CopyResumePlan CanResume(long sourceLength, long destinationLength, long safeResumeOffset) =>
        new()
        {
            Kind = CopyResumePlanKind.CanResume,
            SourceLength = sourceLength,
            DestinationLength = destinationLength,
            SafeResumeOffset = safeResumeOffset,
        };

    public static CopyResumePlan CannotResume(
        long sourceLength,
        long destinationLength,
        string reason,
        CopyResumeReadFailureSide? readFailureSide = null) =>
        new()
        {
            Kind = CopyResumePlanKind.CannotResume,
            SourceLength = sourceLength,
            DestinationLength = destinationLength,
            Reason = reason,
            ReadFailureSide = readFailureSide,
        };

    public static CopyResumePlan AlreadyComplete(long sourceLength, long destinationLength, string reason) =>
        new()
        {
            Kind = CopyResumePlanKind.AlreadyComplete,
            SourceLength = sourceLength,
            DestinationLength = destinationLength,
            SafeResumeOffset = destinationLength,
            Reason = reason,
        };
}
