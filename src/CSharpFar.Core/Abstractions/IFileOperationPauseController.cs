namespace CSharpFar.Core.Abstractions;

public interface IFileOperationPauseController
{
    void WaitIfPaused(CancellationToken cancellationToken);
}
