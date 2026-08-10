namespace CSharpFar.Core.Models;

public sealed class FilePanelState
{
    private PanelLocation _currentLocation = PanelLocation.Local(string.Empty);

    public string CurrentDirectory
    {
        get => _currentLocation.SourcePath;
        internal set => _currentLocation = PanelLocation.Local(value);
    }

    public PanelLocation CurrentLocation
    {
        get => _currentLocation;
        internal set => _currentLocation = value;
    }

    public PanelSourceId SourceId
    {
        get => _currentLocation.SourceId;
        internal set => _currentLocation = new PanelLocation(value, _currentLocation.SourcePath);
    }

    public string SourcePath
    {
        get => _currentLocation.SourcePath;
        internal set => _currentLocation = new PanelLocation(_currentLocation.SourceId, value);
    }

    public List<FilePanelItem> Items { get; } = new();
    public HashSet<string> SelectedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<PanelLocation> SelectedLocations { get; } = new();
    public int CursorIndex { get; set; }
    public int ScrollOffset { get; set; }
    public SortMode SortMode { get; set; }
    public bool SortDescending { get; set; }
    public PanelSummary? Summary { get; internal set; }
    public PanelAutoRefreshState? AutoRefreshState { get; internal set; }
    public PanelProviderCapabilities ProviderCapabilities { get; internal set; } =
        PanelProviderCapabilities.LocalFileSystem;
    public PanelLoadError? LoadError { get; internal set; }
    public string? DisplayTitle { get; internal set; }
    public bool ShowCurrentItemFullPath { get; internal set; }
    public PanelContentKind ContentKind { get; internal set; } = PanelContentKind.Source;
    public SearchRequest? SearchRequest { get; set; }
    public bool SearchWasCancelled { get; set; }
}
