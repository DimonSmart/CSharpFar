namespace CSharpFar.Core.Models;

public sealed class FilePanelState
{
    public required string CurrentDirectory { get; set; }
    public List<FilePanelItem> Items { get; } = new();
    public HashSet<string> SelectedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int CursorIndex { get; set; }
    public int ScrollOffset { get; set; }
    public SortMode SortMode { get; set; }
    public bool SortDescending { get; set; }
    public PanelSummary? Summary { get; set; }
    public PanelAutoRefreshState? AutoRefreshState { get; set; }
    public PanelProviderCapabilities ProviderCapabilities { get; set; } =
        PanelProviderCapabilities.LocalFileSystem;
    public string? DisplayTitle { get; set; }
    public bool ShowCurrentItemFullPath { get; set; }
    public SearchRequest? SearchRequest { get; set; }
    public bool SearchWasCancelled { get; set; }
}
