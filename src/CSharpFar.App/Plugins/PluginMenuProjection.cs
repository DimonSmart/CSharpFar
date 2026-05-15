namespace CSharpFar.App.Plugins;

public sealed record PluginMenuProjection(
    Guid PluginId,
    Guid ItemId,
    string Text,
    char? HotKey);
