namespace CSharpFar.Core.Models;

public readonly record struct PanelSourceId(string Value)
{
    public static PanelSourceId Local { get; } = new("local");
    public static PanelSourceId SearchResults { get; } = new("search");

    public static PanelSourceId Plugin(Guid pluginId, string panelId)
    {
        if (pluginId == Guid.Empty)
            throw new ArgumentException("Plugin id is required.", nameof(pluginId));

        if (string.IsNullOrWhiteSpace(panelId))
            throw new ArgumentException("Plugin panel id is required.", nameof(panelId));

        return new PanelSourceId($"plugin:{pluginId:D}:{panelId}");
    }

    public override string ToString() => Value;
}
