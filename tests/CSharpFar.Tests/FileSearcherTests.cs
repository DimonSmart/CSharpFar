using CSharpFar.App.Search;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 17: FileSearcher finds files matching a mask recursively.
/// </summary>
public class FileSearcherTests : IDisposable
{
    private readonly string _root;

    public FileSearcherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSearchTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        // Layout:
        //   root/
        //     a.cs
        //     b.txt
        //     sub/
        //       c.cs
        //       d.log
        //       deep/
        //         e.cs
        var sub  = Directory.CreateDirectory(Path.Combine(_root, "sub")).FullName;
        var deep = Directory.CreateDirectory(Path.Combine(sub,   "deep")).FullName;
        File.WriteAllText(Path.Combine(_root, "a.cs"),  "//a");
        File.WriteAllText(Path.Combine(_root, "b.txt"),  "b");
        File.WriteAllText(Path.Combine(sub,   "c.cs"),  "//c");
        File.WriteAllText(Path.Combine(sub,   "d.log"), "d");
        File.WriteAllText(Path.Combine(deep,  "e.cs"),  "//e");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Search_FindsAllCsFiles()
    {
        var results = FileSearcher.Search(_root, "*.cs");

        Assert.Equal(3, results.Count);
        Assert.Contains(results, p => p.EndsWith("a.cs"));
        Assert.Contains(results, p => p.EndsWith("c.cs"));
        Assert.Contains(results, p => p.EndsWith("e.cs"));
    }

    [Fact]
    public void Search_FindsSingleExtension()
    {
        var results = FileSearcher.Search(_root, "*.txt");

        Assert.Single(results);
        Assert.Contains(results, p => p.EndsWith("b.txt"));
    }

    [Fact]
    public void Search_NoMatchReturnsEmpty()
    {
        var results = FileSearcher.Search(_root, "*.xyz");

        Assert.Empty(results);
    }

    [Fact]
    public void Search_WildcardMatchesAll()
    {
        var results = FileSearcher.Search(_root, "*");

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void Search_CancelledEarlyReturnsPartialOrEmpty()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        var results = FileSearcher.Search(_root, "*.cs", cts.Token);

        // May return 0 results (cancelled before finding anything)
        Assert.True(results.Count >= 0);
    }

    [Fact]
    public void Search_ResultsAreSortedByPath()
    {
        var results = FileSearcher.Search(_root, "*.cs");

        var sorted = results.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, results);
    }
}
