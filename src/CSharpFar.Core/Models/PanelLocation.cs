namespace CSharpFar.Core.Models;

public readonly record struct PanelLocation(PanelSourceId SourceId, string SourcePath)
{
    public static PanelLocation Local(string path) => new(PanelSourceId.Local, path);
    public static PanelLocation SearchResult(string path) => new(PanelSourceId.SearchResults, path);

    public string SelectionKey => $"{SourceId.Value}\n{SourcePath}";
}
