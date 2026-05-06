namespace CSharpFar.Console.Models;

public readonly struct Rect
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public int Right => X + Width;
    public int Bottom => Y + Height;

    public Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public bool Contains(int x, int y) =>
        x >= X && x < Right && y >= Y && y < Bottom;

    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}
