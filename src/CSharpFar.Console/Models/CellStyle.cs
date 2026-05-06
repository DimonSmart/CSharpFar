namespace CSharpFar.Console.Models;

public readonly struct CellStyle
{
    public ConsoleColor Foreground { get; init; }
    public ConsoleColor Background { get; init; }

    public CellStyle(ConsoleColor foreground, ConsoleColor background)
    {
        Foreground = foreground;
        Background = background;
    }

    public static CellStyle Default => new(ConsoleColor.Gray, ConsoleColor.Black);
}
