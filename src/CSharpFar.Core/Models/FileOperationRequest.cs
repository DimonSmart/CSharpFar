using CSharpFar.Core.Abstractions;

namespace CSharpFar.Core.Models;

public sealed record FileOperationRequest
{
    public required FileOperationKind Kind { get; init; }
    public required IReadOnlyList<PanelLocation> Sources { get; init; }
    // For templates, SourcePath is an unevaluated destination expression.
    public PanelLocation? Destination { get; init; }
    public bool UseDestinationTemplate { get; init; }
    public required FileOperationOptions Options { get; init; }
    public IFileOperationPauseController? PauseController { get; init; }
}
