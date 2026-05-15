namespace CSharpFar.Plugin.Abstractions;

public sealed record PluginPanelInfo
{
    public required string Format { get; init; }
    public required string Title { get; init; }
    public required string CurrentDirectory { get; init; }
    public string? ShortcutData { get; init; }
    public long? FreeSize { get; init; }
    public IReadOnlyDictionary<string, string> KeyBarLabels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
