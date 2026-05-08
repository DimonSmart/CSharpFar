namespace CSharpFar.Core.Models;

public sealed class ScrollState
{
    public required int TotalItems        { get; init; }
    public required int ViewportItems     { get; init; }
    public required int FirstVisibleIndex { get; init; }
}
