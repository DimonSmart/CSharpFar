using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public enum ConflictChoice
{
    Overwrite,
    Skip,
    Cancel,
}

internal static class FileOperationServiceTestExtensions
{
    public static Task<FileOperationResult> CopyAsync(
        this FileOperationService service,
        IReadOnlyList<string> sources,
        string destination,
        Action<string>? onProgress = null,
        Func<string, ConflictChoice>? onConflict = null,
        CancellationToken cancellationToken = default)
    {
        return ThrowIfCancelledAsync(service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = sources,
                Destination = destination,
                Options = new FileOperationOptions(),
            },
            new Progress<FileOperationProgress>(p => onProgress?.Invoke(Path.GetFileName(p.CurrentPath))),
            new TestConflictResolver(onConflict),
            cancellationToken));
    }

    public static Task<FileOperationResult> MoveAsync(
        this FileOperationService service,
        IReadOnlyList<string> sources,
        string destination,
        Func<string, ConflictChoice>? onConflict = null,
        CancellationToken cancellationToken = default)
    {
        return ThrowIfCancelledAsync(service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = sources,
                Destination = destination,
                Options = new FileOperationOptions(),
            },
            null,
            new TestConflictResolver(onConflict),
            cancellationToken));
    }

    public static Task<FileOperationResult> DeleteAsync(
        this FileOperationService service,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        return ThrowIfCancelledAsync(service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Delete,
                Sources = paths,
                Options = new FileOperationOptions { UseRecycleBinForDelete = false },
            },
            null,
            new TestConflictResolver(null),
            cancellationToken));
    }

    public static void CreateDirectory(this FileOperationService service, string path)
    {
        service.ExecuteAsync(
                new FileOperationRequest
                {
                    Kind = FileOperationKind.CreateDirectory,
                    Sources = [],
                    Destination = path,
                    Options = new FileOperationOptions(),
                },
                null,
                new TestConflictResolver(null))
            .GetAwaiter()
            .GetResult();
    }

    private sealed class TestConflictResolver : IFileOperationConflictResolver
    {
        private readonly Func<string, ConflictChoice>? _onConflict;

        public TestConflictResolver(Func<string, ConflictChoice>? onConflict)
        {
            _onConflict = onConflict;
        }

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            return (_onConflict?.Invoke(conflict.DestinationPath) ?? ConflictChoice.Overwrite) switch
            {
                ConflictChoice.Overwrite => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite),
                ConflictChoice.Skip => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Skip),
                ConflictChoice.Cancel => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel),
                _ => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel),
            };
        }
    }

    private static async Task<FileOperationResult> ThrowIfCancelledAsync(Task<FileOperationResult> task)
    {
        FileOperationResult result = await task;
        if (result.Cancelled)
            throw new OperationCanceledException();
        return result;
    }
}
