namespace CSharpFar.Core.Menu;

public sealed record MenuCommandResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
