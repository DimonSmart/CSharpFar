using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class FarWildcardNameTransformerTests
{
    public static TheoryData<string, string, string> Cases => new()
    {
        { "whatever", "*", "whatever" },
        { "1", "A?Z*", "AZ" }, { "12", "A?Z*", "A2Z" }, { "1234", "A?Z*", "A2Z4" },
        { "a", "*.txt", "a.txt" }, { "b.dat", "*.txt", "b.txt" }, { "c.x.y", "*.txt", "c.x.txt" },
        { "a", "*?.bak", "a.bak" }, { "b.dat", "*?.bak", "b.dat.bak" }, { "c.x.y", "*?.bak", "c.x.y.bak" },
        { "a.b.c", "?????.?????", "a.b" }, { "part1.part2.part3", "?????.?????", "part1.part2" },
        { "abcd_12345.txt", "*_NEW.*", "abcd_NEW.txt" },
        { "abc_newt_1.dat", "*_NEW.*", "abc_newt_NEW.dat" },
        { "abcd_123.a_b", "*_NEW.*", "abcd_123.a_NEW" },
        { "part1.part2", "?x.????999.*rForTheCourse", "px.part999.rForTheCourse" },
        { "part1.part2.part3", "?x.????999.*rForTheCourse", "px.part999.parForTheCourse" },
        { "a.b.CarPart3BEER", "?x.????999.*rForTheCourse", "ax.b999.CarParForTheCourse" },
        { "1.2", "*.*.2", "1.2.2" }, { "1.2", "test.*", "test.2" },
        { "1.2", "t*?.", "t.2" }, { "1.2", "t?*.", "t" }, { "1.2", "t*?.*", "t.2" },
        { "1.2", "*.*.*.txt", "1.2..txt" },
        { "1.2", "[a-cf]*.txt", "[a-cf].txt" },
        { "1.2", "*[a-cf].t[]x[]t", "1.2[a-cf].t[]x[]t" },
        { "1.2", "t[est.txt", "t[est.txt" },
        { "1.2", "*a*a*a*a*a*a*a*a*b", "1.2aaaaaaaab" },
        { "1.2", "[t-]*", "[t-].2" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Transform_MatchesFarConvertWildcards(string source, string pattern, string expected) =>
        Assert.Equal(expected, FarWildcardNameTransformer.Transform(source, pattern));
}
