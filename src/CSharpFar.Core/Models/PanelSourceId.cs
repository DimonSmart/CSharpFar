namespace CSharpFar.Core.Models;

public readonly record struct PanelSourceId(string Value)
{
    public static PanelSourceId Local { get; } = new("local");
    public static PanelSourceId SearchResults { get; } = new("search");

    public static PanelSourceId Sftp(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("SFTP connection id is required.", nameof(connectionId));

        return new PanelSourceId($"sftp:{connectionId}");
    }

    public override string ToString() => Value;
}
