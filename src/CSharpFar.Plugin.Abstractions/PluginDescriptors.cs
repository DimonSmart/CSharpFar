namespace CSharpFar.Plugin.Abstractions;

public sealed record PluginDescriptor
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Version { get; init; }
}

public sealed record PluginCommandDescriptor
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required Func<IPluginCommandContext, CancellationToken, ValueTask> ExecuteAsync { get; init; }
}

public sealed record PluginMenuItemDescriptor
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string CommandId { get; init; }

    public char? HotKey { get; init; }
}
