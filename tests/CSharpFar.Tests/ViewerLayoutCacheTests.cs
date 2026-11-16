using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class ViewerLayoutCacheTests
{
    [Fact]
    public void Cache_RefreshesRecencyAndEvictsLeastRecentlyUsedEntry()
    {
        var cache = new ViewerLayoutCache<int, string>(2);
        cache.Set(1, "first");
        cache.Set(2, "second");

        Assert.True(cache.TryGet(1, out string? value));
        Assert.Equal("first", value);

        cache.Set(3, "third");

        Assert.False(cache.TryGet(2, out _));
        Assert.True(cache.TryGet(1, out value));
        Assert.Equal("first", value);
        Assert.True(cache.TryGet(3, out value));
        Assert.Equal("third", value);

        cache.Clear();
        Assert.False(cache.TryGet(1, out _));
    }
}
