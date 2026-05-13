namespace CSharpFar.Core.Models;

public sealed class FilePanelState
{
    private PanelLocation _currentLocation = PanelLocation.Local(string.Empty);

    public required string CurrentDirectory
    {
        get => _currentLocation.SourcePath;
        set => _currentLocation = PanelLocation.Local(value);
    }

    public PanelLocation CurrentLocation
    {
        get => _currentLocation;
        set => _currentLocation = value;
    }

    public PanelSourceId SourceId
    {
        get => _currentLocation.SourceId;
        set => _currentLocation = new PanelLocation(value, _currentLocation.SourcePath);
    }

    public string SourcePath
    {
        get => _currentLocation.SourcePath;
        set => _currentLocation = new PanelLocation(_currentLocation.SourceId, value);
    }

    public List<FilePanelItem> Items { get; } = new();
    public HashSet<string> SelectedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<PanelLocation> SelectedLocations { get; } = new();
    public int CursorIndex { get; set; }
    public int ScrollOffset { get; set; }
    public SortMode SortMode { get; set; }
    public bool SortDescending { get; set; }
    public PanelSummary? Summary { get; set; }
    public PanelAutoRefreshState? AutoRefreshState { get; set; }
    public PanelProviderCapabilities ProviderCapabilities { get; set; } =
        PanelProviderCapabilities.LocalFileSystem;
    public PanelLoadError? LoadError { get; set; }
    public string? DisplayTitle { get; set; }
    public bool ShowCurrentItemFullPath { get; set; }
    public SearchRequest? SearchRequest { get; set; }
    public bool SearchWasCancelled { get; set; }
}
