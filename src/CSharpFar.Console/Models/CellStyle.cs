namespace CSharpFar.Console.Models;

[Flags]
public enum TextAttributes
{
    None = 0,
    Bold = 1,
    Underline = 2,
    Reverse = 4,
}

public readonly struct CellStyle
{
    public ConsoleColor Foreground { get; init; }
    public ConsoleColor Background { get; init; }
    public TextAttributes Attributes { get; init; }

    public CellStyle(ConsoleColor foreground, ConsoleColor background)
        : this(foreground, background, TextAttributes.None)
    {
    }

    public CellStyle(ConsoleColor foreground, ConsoleColor background, TextAttributes attributes)
    {
        Foreground = foreground;
        Background = background;
        Attributes = attributes;
    }

    public static CellStyle Default => new(ConsoleColor.Gray, ConsoleColor.Black);
}
