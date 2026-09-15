using System.Text;
using CSharpFar.App.Viewer;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class MarkdownInlinePresentationTests
{
    [Theory]
    [InlineData("**bold**", "bold", ViewerTextStyle.Bold)]
    [InlineData("*italic*", "italic", ViewerTextStyle.Italic)]
    [InlineData("`code`", "code", ViewerTextStyle.InlineCode)]
    [InlineData("[link](url)", "link", ViewerTextStyle.Link)]
    public void Transform_SimpleConstruct_HidesMarkersAndAddsStyle(
        string source,
        string expected,
        object style)
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(source);

        Assert.Equal(expected, result.Text);
        PresentedStyleSpan span = Assert.Single(result.StyleSpans);
        Assert.Equal(new PresentedStyleSpan(0, expected.Length, (ViewerTextStyle)style), span);
        PresentedSourceSpan mapping = Assert.Single(result.SourceSpans);
        Assert.Equal(expected.Length, mapping.SourceLength);
        Assert.Equal(expected.Length, mapping.PresentedLength);
    }

    [Fact]
    public void Transform_MultipleIndependentConstructs_ProducesIndependentSpans()
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(
            "**bold** and `code` and [link](url) and *italic*");

        Assert.Equal("bold and code and link and italic", result.Text);
        Assert.Equal(
            [ViewerTextStyle.Bold, ViewerTextStyle.InlineCode, ViewerTextStyle.Link, ViewerTextStyle.Italic],
            result.StyleSpans.Select(x => x.Style));
        Assert.Equal([0, 9, 18, 27], result.StyleSpans.Select(x => x.Start));
    }

    [Fact]
    public void Transform_MultipleElementsOfSameType_ProducesSeparateSpans()
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform("`one` and `two`");

        Assert.Equal("one and two", result.Text);
        Assert.Equal(2, result.StyleSpans.Count);
        Assert.All(result.StyleSpans, span => Assert.Equal(ViewerTextStyle.InlineCode, span.Style));
    }

    [Fact]
    public void Transform_MultiBacktickCode_AllowsShorterBacktickRunInside()
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform("``text with ` inside``");

        Assert.Equal("text with ` inside", result.Text);
        Assert.Equal(ViewerTextStyle.InlineCode, Assert.Single(result.StyleSpans).Style);
    }

    [Fact]
    public void Transform_CodeContentIsNotParsedRecursively()
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform("`**not bold**`");

        Assert.Equal("**not bold**", result.Text);
        Assert.Equal(ViewerTextStyle.InlineCode, Assert.Single(result.StyleSpans).Style);
    }

    [Theory]
    [InlineData("**unfinished")]
    [InlineData("*unfinished")]
    [InlineData("`unfinished")]
    [InlineData("[unfinished")]
    [InlineData("\\*not italic*")]
    [InlineData("\\`not code`")]
    [InlineData("\\[not link](url)")]
    [InlineData("__bold__")]
    [InlineData("_some_variable_")]
    [InlineData("***combined***")]
    [InlineData("![image](url)")]
    public void Transform_UnsupportedOrMalformedContent_RemainsRaw(string source)
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(source);

        Assert.Equal(source, result.Text);
        Assert.Empty(result.StyleSpans);
        Assert.False(result.Changed);
    }

    [Fact]
    public void Provider_LinkMapsOnlyVisibleLabel()
    {
        const string sourceText = "See [documentation](README.md).";
        ScannedLine source = Line(sourceText, 0);
        var provider = new MarkdownViewerPresentationProvider();

        PresentedLine result = provider.Present(new ViewerPresentationContext([source], [], [], 80)).Single();

        Assert.Equal("See documentation.", result.Text);
        int labelSource = sourceText.IndexOf("documentation", StringComparison.Ordinal);
        int urlSource = sourceText.IndexOf("README.md", StringComparison.Ordinal);
        Assert.True(result.TryMapSourceRange(labelSource, "documentation".Length, out int labelPresented, out int labelLength));
        Assert.Equal(4, labelPresented);
        Assert.Equal("documentation".Length, labelLength);
        Assert.False(result.TryMapSourceRange(urlSource, "README.md".Length, out _, out _));
        Assert.Equal(ViewerTextStyle.Link, Assert.Single(result.StyleSpans).Style);
    }

    [Fact]
    public void Provider_TableTransformsCellsAndUsesDisplayedWidths()
    {
        ScannedLine[] lines = Lines(
            "| Name | Description |",
            "|---|---|",
            "| `TaskRelatedIntent` | **Task metadata** |");
        var provider = new MarkdownViewerPresentationProvider();

        IReadOnlyList<PresentedLine> result = provider.Present(new ViewerPresentationContext(lines, [], [], 120));

        Assert.Contains("TaskRelatedIntent", result[2].Text);
        Assert.Contains("Task metadata", result[2].Text);
        Assert.DoesNotContain("`", result[2].Text);
        Assert.DoesNotContain("**", result[2].Text);
        Assert.Equal(
            [ViewerTextStyle.InlineCode, ViewerTextStyle.Bold],
            result[2].StyleSpans.Select(x => x.Style));
        Assert.All(result[2].StyleSpans, span =>
        {
            Assert.True(span.Start >= 0);
            Assert.True(span.Length > 0);
            Assert.True(span.Start + span.Length <= result[2].Text.Length);
        });
    }

    [Fact]
    public void Provider_WideUnicodeKeepsUtf16StyleCoordinates()
    {
        ScannedLine source = Line("**界**", 0);
        var provider = new MarkdownViewerPresentationProvider();

        PresentedLine result = provider.Present(new ViewerPresentationContext([source], [], [], 20)).Single();

        Assert.Equal("界", result.Text);
        Assert.Equal(new PresentedStyleSpan(0, 1, ViewerTextStyle.Bold), Assert.Single(result.StyleSpans));
        Assert.True(result.TryMapSourceRange(2, 1, out int start, out int length));
        Assert.Equal(0, start);
        Assert.Equal(1, length);
    }

    [Fact]
    public void RawLine_HasNoStyleSpans()
    {
        PresentedLine raw = PresentedLine.Raw(Line("**bold**", 0));

        Assert.Equal("**bold**", raw.Text);
        Assert.Empty(raw.StyleSpans);
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
