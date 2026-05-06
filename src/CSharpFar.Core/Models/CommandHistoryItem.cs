namespace CSharpFar.Core.Models;

public sealed class CommandHistoryItem
{
    public required string Command { get; init; }
    public required string WorkingDirectory { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
