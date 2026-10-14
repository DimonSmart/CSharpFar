using CSharpFar.Core.Abstractions;

namespace CSharpFar.Plugin.Abstractions;

public sealed record PluginDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Version { get; init; }
}

public enum PluginMenuPlacement
{
    Plugins,
    Disk,
}

public sealed record PluginCommandDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required Func<IPluginCommandContext, CancellationToken, ValueTask<PluginCommandResult>> ExecuteAsync { get; init; }
}

public sealed record PluginMenuItemDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string CommandId { get; init; }
    public char? HotKey { get; init; }
    /// <summary>Optional stable action identity retained for existing application routes.</summary>
    public Guid? ActionId { get; init; }
    public PluginMenuPlacement Placement { get; init; } = PluginMenuPlacement.Plugins;
}

public enum PluginCommandResultKind
{
    Completed,
    OpenPanel,
    Cancelled,
    Failed,
}

public sealed record PluginPanelOpenRequest
{
    public required IFilePanelSource Source { get; init; }
    public required string InitialDirectory { get; init; }
    public required string Title { get; init; }
    public string? Format { get; init; }
    public string? ShortcutData { get; init; }
    public long? FreeSize { get; init; }
    public IReadOnlyDictionary<string, string> KeyBarLabels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record PluginCommandResult
{
    private PluginCommandResult(PluginCommandResultKind kind, PluginPanelOpenRequest? panel, string? error)
        => (Kind, Panel, Error) = (kind, panel, error);

    public PluginCommandResultKind Kind { get; }
    public PluginPanelOpenRequest? Panel { get; }
    public string? Error { get; }

    public static PluginCommandResult Completed() => new(PluginCommandResultKind.Completed, null, null);
    public static PluginCommandResult Cancelled() => new(PluginCommandResultKind.Cancelled, null, null);
    public static PluginCommandResult Failed(string error) => new(PluginCommandResultKind.Failed, null, error);
    public static PluginCommandResult OpenPanel(PluginPanelOpenRequest panel) =>
        new(PluginCommandResultKind.OpenPanel, panel ?? throw new ArgumentNullException(nameof(panel)), null);
}
