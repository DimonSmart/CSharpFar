namespace CSharpFar.Core.Comparison;

public sealed record FileCompareOutcome(bool Equal, string? Message = null, long ComparedBytes = 0);

public interface IFileComparer
{
    FileCompareOutcome Compare(FileEntry left, FileEntry right, CancellationToken cancellationToken = default);
}
