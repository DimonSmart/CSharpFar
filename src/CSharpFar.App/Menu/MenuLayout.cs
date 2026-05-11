using CSharpFar.Console.Models;

namespace CSharpFar.App.Menu;

public sealed record MenuLayout
{
    public required IReadOnlyList<Rect> TopItemBounds { get; init; }
    public Rect? DropdownBounds { get; init; }
    public int DropdownFirstVisibleItemIndex { get; init; }
}
