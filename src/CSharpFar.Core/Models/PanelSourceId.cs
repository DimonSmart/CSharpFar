namespace CSharpFar.Core.Models;

public readonly record struct PanelSourceId(string Value)
{
    public static PanelSourceId Local { get; } = new("local");
    public static PanelSourceId SearchResults { get; } = new("search");

    public static PanelSourceId Module(Guid moduleId, string panelId)
    {
        if (moduleId == Guid.Empty)
            throw new ArgumentException("Module id is required.", nameof(moduleId));

        if (string.IsNullOrWhiteSpace(panelId))
            throw new ArgumentException("Module panel id is required.", nameof(panelId));

        return new PanelSourceId($"plugin:{moduleId:D}:{panelId}");
    }

    public override string ToString() => Value;
}
