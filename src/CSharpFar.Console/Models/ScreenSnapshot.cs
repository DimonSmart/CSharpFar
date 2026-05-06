namespace CSharpFar.Console.Models;

public readonly struct SnapshotCell
{
    public char Character { get; init; }
    public ConsoleColor Foreground { get; init; }
    public ConsoleColor Background { get; init; }
}

public sealed class ScreenSnapshot
{
    public Rect Region { get; }
    public SnapshotCell[,] Cells { get; }

    public ScreenSnapshot(Rect region, SnapshotCell[,] cells)
    {
        Region = region;
        Cells = cells;
    }
}
