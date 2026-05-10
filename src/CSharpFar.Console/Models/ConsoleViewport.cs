namespace CSharpFar.Console.Models;

public readonly record struct ConsoleViewport(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width - 1;
    public int Bottom => Top + Height - 1;
    public ConsoleSize Size => new(Width, Height);

    public bool ContainsAbsolute(int x, int y) =>
        x >= Left &&
        x <= Right &&
        y >= Top &&
        y <= Bottom;
}
