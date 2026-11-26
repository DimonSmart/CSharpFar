namespace CSharpFar.Core.Models;

public sealed record SearchResultItem
{
    public required string FullPath { get; init; }
    public PanelSourceId SourceId { get; init; } = PanelSourceId.Local;
    public PanelLocation Location => new(SourceId, FullPath);
    public required string Name { get; init; }
    public required SearchResultItemKind Kind { get; init; }
    public long? Size { get; init; }
    public DateTime LastWriteTime { get; init; }
    public FileAttributes Attributes { get; init; }
    public string? MatchedTextPreview { get; init; }
    public int? LineNumber { get; init; }
}
