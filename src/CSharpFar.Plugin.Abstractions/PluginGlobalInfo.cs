namespace CSharpFar.Plugin.Abstractions;

public sealed record PluginGlobalInfo
{
    public required Guid PluginId { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public Version Version { get; init; } = new(1, 0, 0);
}
