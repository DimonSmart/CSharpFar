using CSharpFar.App;
using CSharpFar.App.Viewer;
using CSharpFar.Console.Models;

namespace CSharpFar.Tests;

public class MarkdownHeadingPresentationTests
{
    [Theory]
    [InlineData("# H1", "Heading1")]
    [InlineData("## H2", "Heading2")]
    [InlineData("### H3", "Heading3")]
    [InlineData("#### H4", "Heading4")]
    [InlineData("##### H5", "Heading5")]
    [InlineData("###### H6", "Heading6")]
    public void Provider_PresentsAllAtxLevels(string source, string styleName)
    {
        PresentedLine result = Present(source);

        Assert.Equal(source[(source.IndexOf(' ') + 1)..], result.Text);
        PresentedStyleSpan span = Assert.Single(result.StyleSpans);
        Assert.Equal(styleName, span.Style.ToString());
        Assert.Equal(0, span.Start);
        Assert.Equal(result.Text.Length, span.Length);
    }

    [Theory]
    [InlineData("#NotHeading")]
    [InlineData("####### NotHeading")]
    [InlineData("    # FourSpaces")]
    [InlineData("\\# Escaped")]
    [InlineData("\t# LeadingTab")]
    public void Provider_LeavesInvalidOpeningSyntaxRaw(string source)
    {
        PresentedLine result = Present(source);

        Assert.Equal(source, result.Text);
        Assert.Empty(result.StyleSpans);
        PresentedSourceSpan span = Assert.Single(result.SourceSpans);
        Assert.Equal(new PresentedSourceSpan(0, source.Length, 0, source.Length), span);
    }

    [Fact]
    public void Provider_AcceptsThreeLeadingSpacesAndTabSeparator()
    {
        PresentedLine result = Present("   ##\tHeading");

        Assert.Equal("Heading", result.Text);
        Assert.Equal(ViewerTextStyle.Heading2, Assert.Single(result.StyleSpans).Style);
        Assert.True(result.TryMapSourceRange(6, 7, out int presentedStart, out int presentedLength));
        Assert.Equal("Heading", result.Text.Substring(presentedStart, presentedLength));
    }

    [Theory]
    [InlineData("#")]
    [InlineData("## ")]
    [InlineData("### ###")]
    public void Provider_SupportsEmptyHeadings(string source)
    {
        PresentedLine result = Present(source);

        Assert.Empty(result.Text);
        Assert.Empty(result.SourceSpans);
        Assert.Empty(result.StyleSpans);
    }

    [Theory]
    [InlineData("## Heading ##", "Heading")]
    [InlineData("##### Heading ##", "Heading")]
    [InlineData("## Heading ###    ", "Heading")]
    [InlineData("###    Heading     ", "Heading")]
    [InlineData("###    Heading     ###", "Heading")]
    public void Provider_TrimsHeadingWhitespaceAndClosingMarkers(string source, string expected)
    {
        PresentedLine result = Present(source);

        Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("## C# language", "C# language")]
    [InlineData("# foo#", "foo#")]
    [InlineData("### foo ### b", "foo ### b")]
    [InlineData("### foo \\###", "foo \\###")]
    public void Provider_PreservesHashesThatAreContent(string source, string expected)
    {
        Assert.Equal(expected, Present(source).Text);
    }

    [Fact]
    public void Provider_ComposesHeadingStyleWithInlineStylesWithoutOverlap()
    {
        PresentedLine result = Present(
            "## Use **Factory** with `TaskRelatedIntent` and [documentation](README.md)");

        Assert.Equal("Use Factory with TaskRelatedIntent and documentation", result.Text);
        ViewerTextStyle[] expectedStyles =
        [
            ViewerTextStyle.Heading2,
            ViewerTextStyle.Bold,
            ViewerTextStyle.Heading2,
            ViewerTextStyle.InlineCode,
            ViewerTextStyle.Heading2,
            ViewerTextStyle.Link,
        ];
        Assert.Equal(expectedStyles, result.StyleSpans.Select(span => span.Style));

        int previousEnd = 0;
        foreach (PresentedStyleSpan span in result.StyleSpans)
        {
            Assert.True(span.Length > 0);
            Assert.True(span.Start >= previousEnd);
            Assert.True(span.Start + span.Length <= result.Text.Length);
            previousEnd = span.Start + span.Length;
        }
    }

    [Fact]
    public void Provider_MapsOnlyVisibleHeadingContent()
    {
        const string source = "## Architecture ##";
        PresentedLine result = Present(source);
        int contentStart = source.IndexOf("Architecture", StringComparison.Ordinal);
        int closingStart = source.LastIndexOf("##", StringComparison.Ordinal);

        Assert.True(result.TryMapSourceRange(contentStart, "Architecture".Length, out int start, out int length));
        Assert.Equal("Architecture", result.Text.Substring(start, length));
        Assert.False(result.TryMapSourceRange(0, 2, out _, out _));
        Assert.False(result.TryMapSourceRange(closingStart, 2, out _, out _));
        Assert.False(result.TryMapSourceRange(1, contentStart, out _, out _));
    }

    [Fact]
    public void Provider_PreservesUtf16CoordinatesForUnicodeContent()
    {
        const string source = "## Привет 世界";
        PresentedLine result = Present(source);
        int contentStart = source.IndexOf("Привет", StringComparison.Ordinal);

        Assert.Equal("Привет 世界", result.Text);
        Assert.True(result.TryMapSourceRange(contentStart, "Привет 世界".Length, out int start, out int length));
        Assert.Equal(0, start);
        Assert.Equal("Привет 世界".Length, length);
    }

    [Fact]
    public void Provider_TablePresentationTakesPrecedenceOverHeadingDetection()
    {
        ScannedLine[] source = Lines(
            "| Value |",
            "| --- |",
            "| # not heading |");

        PresentedLine body = Present(source)[2];

        Assert.StartsWith("│ # not heading", body.Text);
        Assert.DoesNotContain(body.StyleSpans, span => IsHeading(span.Style));
    }

    [Fact]
    public void Provider_OversizedHeadingFallsBackToExactRawSource()
    {
        string source = "# " + new string('#', MarkdownViewerPresentationProvider.MaxPresentedLineChars);

        PresentedLine result = Present(source);

        Assert.Equal(source, result.Text);
        Assert.Empty(result.StyleSpans);
    }

    [Fact]
    public void HeadingPalette_ExposesIndependentStylesAndEmphasizesTopLevels()
    {
        CSharpFarPalette palette = CSharpFarPaletteRegistry.Default;

        CellStyle h1 = CSharpFarPaletteStyles.MarkdownHeading1(palette);
        CellStyle h2 = CSharpFarPaletteStyles.MarkdownHeading2(palette);
        CellStyle h3 = CSharpFarPaletteStyles.MarkdownHeading3(palette);
        CellStyle h4 = CSharpFarPaletteStyles.MarkdownHeading4(palette);
        CellStyle h5 = CSharpFarPaletteStyles.MarkdownHeading5(palette);
        CellStyle h6 = CSharpFarPaletteStyles.MarkdownHeading6(palette);

        Assert.Equal(palette.MarkdownHeading1Fg, h1.Foreground);
        Assert.Equal(palette.MarkdownHeading2Fg, h2.Foreground);
        Assert.Equal(palette.MarkdownHeading3Fg, h3.Foreground);
        Assert.Equal(palette.MarkdownHeading4Fg, h4.Foreground);
        Assert.Equal(palette.MarkdownHeading5Fg, h5.Foreground);
        Assert.Equal(palette.MarkdownHeading6Fg, h6.Foreground);
        Assert.True((h1.Attributes & TextAttributes.Bold) != 0);
        Assert.True((h2.Attributes & TextAttributes.Bold) != 0);
    }

    private static bool IsHeading(ViewerTextStyle style) =>
        style is ViewerTextStyle.Heading1 or ViewerTextStyle.Heading2 or ViewerTextStyle.Heading3 or
            ViewerTextStyle.Heading4 or ViewerTextStyle.Heading5 or ViewerTextStyle.Heading6;

    private static PresentedLine Present(string text) => Present(Line(0, text)).Single();

    private static IReadOnlyList<PresentedLine> Present(params ScannedLine[] lines)
    {
        var provider = new MarkdownViewerPresentationProvider();
        return provider.Present(new ViewerPresentationContext(lines, [], [], 120));
    }

    private static ScannedLine[] Lines(params string[] text)
    {
        long offset = 0;
        var result = new ScannedLine[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            result[i] = Line(offset, text[i]);
            offset = result[i].NextOffset;
        }

        return result;
    }

    private static ScannedLine Line(long offset, string text) =>
        new(offset, offset + text.Length + 1, text);
}
