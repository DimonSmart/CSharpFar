namespace CSharpFar.Console.Models;

/// <summary>A physical console cell prepared by the buffered renderer.</summary>
public readonly record struct ConsoleOutputCell(
    char Character,
    ConsoleColor Foreground,
    ConsoleColor Background,
    TextAttributes Attributes);
