namespace CSharpFar.Plugin.Abstractions;

public sealed record PluginInfo
{
    public PluginInfoFlags Flags { get; init; }
    public IReadOnlyList<PluginMenuItem> PluginMenuItems { get; init; } = [];
    public IReadOnlyList<PluginMenuItem> DiskMenuItems { get; init; } = [];
    public IReadOnlyList<PluginMenuItem> ConfigMenuItems { get; init; } = [];
    public IReadOnlyList<string> CommandPrefixes { get; init; } = [];
}

[Flags]
public enum PluginInfoFlags
{
    None = 0,
}
