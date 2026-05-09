namespace CSharpFar.Core.Models;

public sealed record FileOperationItemError
{
    public required string Path { get; init; }
    public required string Message { get; init; }
}
