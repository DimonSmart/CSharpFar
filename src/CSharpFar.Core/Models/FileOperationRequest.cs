using CSharpFar.Core.Abstractions;

namespace CSharpFar.Core.Models;

public sealed record FileOperationRequest
{
    public required FileOperationKind Kind { get; init; }
    public required IReadOnlyList<string> Sources { get; init; }
    public string? Destination { get; init; }
    public required FileOperationOptions Options { get; init; }
    public IFileOperationPauseController? PauseController { get; init; }
}
