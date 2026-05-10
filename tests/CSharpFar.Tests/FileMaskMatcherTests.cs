using CSharpFar.Core.FileMasks;
using CSharpFar.Core.Highlighting;

namespace CSharpFar.Tests;

/// <summary>
/// Unit tests for FarMaskMatcher covering wildcard, list, exclude,
/// groups, regex, %PATHEXT%, and *.* normalization semantics.
/// </summary>
public class FileMaskMatcherTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, MaskGroup> NoGroups =
        new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, MaskGroup> FarGroups =
        FarDefaultHighlightPreset.GroupsByName;

    private static bool Match(string mask, string file,
        IReadOnlyDictionary<string, MaskGroup>? groups = null,
        string? pathExt = null)
    {
        var m = new FarMaskMatcher(pathExt);
        return m.IsMatch(mask, file, groups ?? NoGroups);
    }

    // ── Basic wildcard ────────────────────────────────────────────────────────

    [Fact] public void Star_Cs_Matches_Program_cs()        => Assert.True(Match("*.cs", "Program.cs"));
    [Fact] public void Star_Cs_Matches_PROGRAM_CS()        => Assert.True(Match("*.cs", "PROGRAM.CS"));
    [Fact] public void Star_Cs_NoMatch_Program_txt()       => Assert.False(Match("*.cs", "Program.txt"));
    [Fact] public void Question_Matches_Single_Char()      => Assert.True(Match("file?.txt", "file1.txt"));
    [Fact] public void Question_NoMatch_Empty()            => Assert.False(Match("file?.txt", "file.txt"));
    [Fact] public void Range_c_f_Matches_d()               => Assert.True(Match("[c-f]*.txt", "demo.txt"));
    [Fact] public void Range_c_f_NoMatch_a()               => Assert.False(Match("[c-f]*.txt", "alpha.txt"));

    [Fact]
    public void CaseSensitiveWildcard_DoesNotMatchDifferentCase()
    {
        var matcher = new FarMaskMatcher();

        Assert.False(matcher.IsMatch("*.cs", "PROGRAM.CS", NoGroups, caseSensitive: true));
    }

    // ── *.* normalization ─────────────────────────────────────────────────────

    [Fact] public void StarDotStar_Matches_File_Without_Extension() => Assert.True(Match("*.*", "README"));
    [Fact] public void StarDotStar_Matches_File_With_Extension()    => Assert.True(Match("*.*", "readme.md"));

    // ── Exclude (|) ───────────────────────────────────────────────────────────

    [Fact]
    public void Include_Exclude_Matches_Non_Excluded()
        => Assert.True(Match("*.*|*.bak,*.tmp", "readme.md"));

    [Fact]
    public void Include_Exclude_DoesNotMatch_Excluded()
        => Assert.False(Match("*.*|*.bak,*.tmp", "test.bak"));

    [Fact]
    public void Empty_Include_With_Exclude_Acts_As_Star()
        => Assert.True(Match("|*.bak", "readme.md"));

    [Fact]
    public void Empty_Include_Excludes_Match()
        => Assert.False(Match("|*.bak", "test.bak"));

    [Fact]
    public void Empty_Exclude_After_Pipe_Ignored()
        => Assert.True(Match("*.*|", "README"));

    // ── Groups ────────────────────────────────────────────────────────────────

    [Fact]
    public void Arc_Group_Matches_zip()
        => Assert.True(Match("<arc>", "test.zip", FarGroups));

    [Fact]
    public void Arc_Group_Matches_rar()
        => Assert.True(Match("<arc>", "test.rar", FarGroups));

    [Fact]
    public void Arc_Exclude_rar_Matches_zip()
        => Assert.True(Match("<arc>|*.rar", "test.zip", FarGroups));

    [Fact]
    public void Arc_Exclude_rar_DoesNotMatch_rar()
        => Assert.False(Match("<arc>|*.rar", "test.rar", FarGroups));

    [Fact]
    public void Unknown_Group_Does_Not_Match()
        => Assert.False(Match("<nonexistent>", "test.zip", FarGroups));

    [Fact]
    public void Unknown_Group_With_Exclude_Does_Not_Match()
        => Assert.False(Match("<nonexistent>|*.bak", "readme.md", FarGroups));

    [Fact]
    public void Nested_Group_References_Arc()
    {
        var groups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase)
        {
            ["archives"] = new MaskGroup { Name = "archives", MaskExpression = "*.7z,<arc>" },
            ["arc"]      = new MaskGroup { Name = "arc",      MaskExpression = "*.zip,*.rar" },
        };
        Assert.True(Match("<archives>", "file.zip", groups));
        Assert.True(Match("<archives>", "file.7z",  groups));
        Assert.True(Match("<archives>", "file.rar", groups));
    }

    [Fact]
    public void Cyclic_Group_Does_Not_Recurse_Forever()
    {
        var groups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new MaskGroup { Name = "a", MaskExpression = "<b>" },
            ["b"] = new MaskGroup { Name = "b", MaskExpression = "<a>" },
        };
        // Should not throw; cyclic group expands to nothing → no match
        Assert.False(Match("<a>", "any.txt", groups));
        Assert.False(Match("<a>|*.bak", "any.txt", groups));
    }

    [Fact]
    public void Matcher_Cache_Uses_Group_Definitions()
    {
        var matcher = new FarMaskMatcher(pathExt: "");
        var zipGroups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase)
        {
            ["arc"] = new MaskGroup { Name = "arc", MaskExpression = "*.zip" },
        };
        var rarGroups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase)
        {
            ["arc"] = new MaskGroup { Name = "arc", MaskExpression = "*.rar" },
        };

        Assert.True(matcher.IsMatch("<arc>", "file.zip", zipGroups));
        Assert.True(matcher.IsMatch("<arc>", "file.rar", rarGroups));
        Assert.False(matcher.IsMatch("<arc>", "file.zip", rarGroups));
    }

    // ── Regex ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Regex_FullMatch_Case_Insensitive()
        => Assert.True(Match("/^pict\\d{1,3}\\.gif$/i", "PICT123.GIF"));

    [Fact]
    public void Regex_Partial_Match()
        => Assert.True(Match("/(eng|rus)/i", "my_rus_file.txt"));

    [Fact]
    public void Regex_PartialMatch_NoAnchor()
        => Assert.True(Match("/readme/i", "README.md"));

    [Fact]
    public void CaseInsensitiveRequest_AppliesToRegexWithoutExplicitFlag()
    {
        var matcher = new FarMaskMatcher();

        Assert.True(matcher.IsMatch("/readme/", "README.md", NoGroups, caseSensitive: false));
    }

    [Fact]
    public void CaseSensitiveRequest_StillHonorsRegexIgnoreCaseFlag()
    {
        var matcher = new FarMaskMatcher();

        Assert.True(matcher.IsMatch("/readme/i", "README.md", NoGroups, caseSensitive: true));
    }

    // ── %PATHEXT% ─────────────────────────────────────────────────────────────

    [Fact]
    public void PathExt_ExpandsFromSnapshot()
    {
        var groups = new Dictionary<string, MaskGroup>(StringComparer.OrdinalIgnoreCase)
        {
            ["exec"] = new MaskGroup
            {
                Name = "exec",
                MaskExpression = "*.exe,*.cmd,*.bat,*.com,%PATHEXT%",
            },
        };
        var matcher = new FarMaskMatcher(pathExt: ".EXE;.BAT;.PS1");
        Assert.True(matcher.IsMatch("<exec>", "script.ps1", groups));
        Assert.False(matcher.IsMatch("<exec>", "script.vbs", groups));
    }

    [Fact]
    public void PathExt_Empty_Does_Not_Crash()
    {
        var groups = FarGroups;
        var matcher = new FarMaskMatcher(pathExt: "");
        // *.exe is still in exec group explicitly
        Assert.True(matcher.IsMatch("<exec>", "app.exe", groups));
    }
}
