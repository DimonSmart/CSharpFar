namespace CSharpFar.Console.Models;

public readonly struct SnapshotCell
{
    public char Character { get; init; }
    public ConsoleColor Foreground { get; init; }
    public ConsoleColor Background { get; init; }
    public TextAttributes Attributes { get; init; }
}

public sealed class ScreenSnapshot
{
    public ConsoleViewport Viewport { get; }
    public Rect Region { get; }
    public SnapshotCell[,] Cells { get; }

    public ScreenSnapshot(ConsoleViewport viewport, Rect region, SnapshotCell[,] cells)
    {
        Viewport = viewport;
        Region = region;
        Cells = cells;
    }
}
