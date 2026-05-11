namespace CSharpFar.FileSystem;

public sealed record CopyResumeOptions
{
    public const int DefaultInitialTailValidationBytes = 1024 * 1024;
    public const int DefaultMinimumValidationRangeBytes = 64 * 1024;
    public const int DefaultMaximumRollbackSearchBytes = 64 * 1024 * 1024;
    public const int DefaultRollbackMultiplier = 4;
    public const int DefaultComparisonBufferBytes = 128 * 1024;

    public static CopyResumeOptions Default { get; } = new();

    public int InitialTailValidationBytes { get; init; } = DefaultInitialTailValidationBytes;
    public int MinimumValidationRangeBytes { get; init; } = DefaultMinimumValidationRangeBytes;
    public int MaximumRollbackSearchBytes { get; init; } = DefaultMaximumRollbackSearchBytes;
    public int RollbackMultiplier { get; init; } = DefaultRollbackMultiplier;
    public int ComparisonBufferBytes { get; init; } = DefaultComparisonBufferBytes;
}
