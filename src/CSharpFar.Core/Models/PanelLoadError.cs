namespace CSharpFar.Core.Models;

public sealed record PanelLoadError
{
    public required string Message { get; init; }
    public required PanelLocation RetryLocation { get; init; }
}
