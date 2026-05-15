namespace CSharpFar.Plugin.Abstractions;

public sealed record PluginMenuItem
{
    public required Guid ItemId { get; init; }
    public required string Text { get; init; }
    public char? HotKey { get; init; }
    public PluginMenuItemFlags Flags { get; init; }
}

[Flags]
public enum PluginMenuItemFlags
{
    None = 0,
}
