using System.Text;
using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class MarkdownLinkPresentationTests
{
    [Fact]
    public void InlineLinkPreservesTargetAndVisibleLabelSpan()
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(
            "See [documentation](https://example.com/docs?q=test#section).");

        Assert.Equal("See documentation.", result.Text);
        PresentedLinkSpan link = Assert.Single(result.LinkSpans);
        Assert.Equal(new PresentedLinkSpan(
            4,
            "documentation".Length,
            "https://example.com/docs?q=test#section"), link);
    }

    [Fact]
    public void MultipleLinksHaveIndependentSemanticSpans()
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(
            "[one](https://one.example) and [two](docs/two.md)");

        Assert.Equal("one and two", result.Text);
        Assert.Equal(2, result.LinkSpans.Count);
        Assert.Equal([0, 8], result.LinkSpans.Select(span => span.Start));
        Assert.Equal(
            ["https://one.example", "docs/two.md"],
            result.LinkSpans.Select(span => span.Target));
    }

    [Fact]
    public void FormattingSyntaxInsideLabelDoesNotLoseLinkSemantics()
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(
            "[**Documentation**](https://example.com)");

        Assert.Equal("**Documentation**", result.Text);
        Assert.Equal(
            new PresentedLinkSpan(0, result.Text.Length, "https://example.com"),
            Assert.Single(result.LinkSpans));
    }

    [Fact]
    public void TableMovesLinkSpanWithCellPaddingButDoesNotIncludePadding()
    {
        ScannedLine[] lines = Lines(
            "| Name | Link |",
            "|---|---:|",
            "| Test | [site](https://example.com) |");
        var provider = new MarkdownViewerPresentationProvider();

        PresentedLine row = provider.Present(
            new ViewerPresentationContext(lines, [], [], 120))[2];

        PresentedLinkSpan link = Assert.Single(row.LinkSpans);
        Assert.Equal("site", row.Text.Substring(link.Start, link.Length));
        Assert.Equal("https://example.com", link.Target);
        Assert.NotEqual(' ', row.Text[link.Start]);
        Assert.NotEqual(' ', row.Text[link.Start + link.Length - 1]);
    }

    [Fact]
    public void RawLineHasNoSemanticLinks()
    {
        PresentedLine raw = PresentedLine.Raw(Line("[site](https://example.com)", 0));

        Assert.Empty(raw.LinkSpans);
    }

    private static ScannedLine Line(string text, long start) =>
        new(start, start + Encoding.UTF8.GetByteCount(text) + 1, text);

    private static ScannedLine[] Lines(params string[] texts)
    {
        var result = new ScannedLine[texts.Length];
        long offset = 0;
        for (int i = 0; i < texts.Length; i++)
        {
            result[i] = Line(texts[i], offset);
            offset = result[i].NextOffset;
        }

        return result;
    }
}
