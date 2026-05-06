namespace CSharpFar.Core.Models;

public sealed class FileHistoryItem
{
    public required string Path { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
