namespace CSharpFar.Core.Models;

public sealed record PanelContent
{
    public PanelContent(
        PanelLocation location,
        IEnumerable<FilePanelItem> items,
        PanelProviderCapabilities capabilities)
    {
        Location = location;
        Items = items.ToArray();
        Capabilities = capabilities;
    }

    public PanelLocation Location { get; }
    public IReadOnlyList<FilePanelItem> Items { get; }
    public PanelProviderCapabilities Capabilities { get; }
    public string? Title { get; init; }
    public bool ShowCurrentItemFullPath { get; init; }
}
