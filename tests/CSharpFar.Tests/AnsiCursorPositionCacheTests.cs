using CSharpFar.Console.Ansi;

namespace CSharpFar.Tests;

public class AnsiCursorPositionCacheTests
{
    [Fact]
    public void TrackWrite_AdvancesCacheSoMoveBackIsNotSkipped()
    {
        var cache = new AnsiCursorPositionCache();
        cache.Set(10, 4);

        cache.TrackWrite(10, 4, textLength: 1, bufferWidth: 80);

        Assert.True(cache.IsAt(11, 4));
        Assert.False(cache.IsAt(10, 4));
    }

    [Theory]
    [InlineData(79, 1)]
    [InlineData(78, 2)]
    public void TrackWrite_ReachingLastColumnInvalidatesCache(int column, int textLength)
    {
        var cache = new AnsiCursorPositionCache();
        cache.Set(column, 4);

        cache.TrackWrite(column, 4, textLength, bufferWidth: 80);

        Assert.Equal(-1, cache.X);
        Assert.Equal(-1, cache.Y);
    }
}
