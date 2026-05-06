namespace CSharpFar.Console.Models;

public readonly struct ConsoleSize
{
    public int Width { get; init; }
    public int Height { get; init; }

    public ConsoleSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public override string ToString() => $"{Width}x{Height}";
}
