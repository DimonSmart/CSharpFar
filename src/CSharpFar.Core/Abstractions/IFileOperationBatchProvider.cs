using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

/// <summary>
/// Optional destination capability. A batch is a private working view of the destination:
/// all writes, deletes and directory changes remain invisible until CommitAsync succeeds.
/// The original source must remain unchanged on AbortAsync or a failed commit.
/// </summary>
public interface IFileOperationBatchProvider
{
    ValueTask<IFileOperationBatch> BeginBatchAsync(
        FileOperationPlan plan, CancellationToken cancellationToken = default);
}

/// <summary>
/// Staged file-system view. Commit and Abort are terminal and must be safe to repeat.
/// Dispose without a successful commit must abort uncommitted changes.
/// </summary>
public interface IFileOperationBatch : IFilePanelSource, IAsyncDisposable
{
    /// <summary>Observe cancellation before final publication. Once publication begins,
    /// finish it or recover; do not report cancellation after a successful commit.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task AbortAsync(CancellationToken cancellationToken = default);
}

/// <summary>Optional diagnostics for a committed batch whose auxiliary cleanup failed.</summary>
public interface IFileOperationBatchDiagnostics
{
    IReadOnlyList<string> Warnings { get; }
}
