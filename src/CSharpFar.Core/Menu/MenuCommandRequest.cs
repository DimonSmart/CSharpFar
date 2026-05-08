namespace CSharpFar.Core.Menu;

public sealed record MenuCommandRequest
{
    public required string CommandId { get; init; }
    public object? Args { get; init; }
}
