namespace CSharpFar.Console.Ansi;

internal sealed class AnsiCursorPositionCache
{
    public int X { get; private set; } = -1;
    public int Y { get; private set; } = -1;

    public bool IsAt(int x, int y) => X == x && Y == y;

    public void Set(int x, int y)
    {
        X = x;
        Y = y;
    }

    public void TrackWrite(int column, int row, int textLength, int bufferWidth)
    {
        int nextColumn = column + textLength;
        if (nextColumn < bufferWidth)
        {
            Set(nextColumn, row);
            return;
        }

        Reset();
    }

    public void Reset()
    {
        X = -1;
        Y = -1;
    }
}
