using System.Text;
using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class MarkdownPresentationLimitTests
{
    private const int Limit = MarkdownViewerPresentationProvider.MaxPresentedLineChars;

    [Fact]
    public void Transform_SourceOverLimitFallsBackToExactRawTextWithoutStyles()
    {
        string source = "**bold**" + new string('x', Limit);

        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(source);

        Assert.Equal(source, result.Text);
        Assert.Empty(result.StyleSpans);
        Assert.False(result.Changed);
        Assert.Equal(
            new PresentedSourceSpan(0, source.Length, 0, source.Length),
            Assert.Single(result.SourceSpans));
    }

    [Fact]
    public void Transform_SourceAtLimitStillUsesInlinePresentation()
    {
        string source = "**bold**" + new string('x', Limit - "**bold**".Length);

        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(source);

        Assert.True(result.Changed);
        Assert.StartsWith("bold", result.Text);
        Assert.DoesNotContain("**", result.Text);
        Assert.Equal(ViewerTextStyle.Bold, Assert.Single(result.StyleSpans).Style);
    }

    [Fact]
    public void Provider_TableCellOverLimitFallsBackToExactRawLine()
    {
        string cell = "**" + new string('x', Limit) + "**";
        ScannedLine[] lines = Lines(
            $"|{cell}|Value|",
            "|---|---|",
            "|ok|ok|");
        var provider = new MarkdownViewerPresentationProvider();

        IReadOnlyList<PresentedLine> result = provider.Present(
            new ViewerPresentationContext(lines, [], [], 80));

        Assert.Equal(lines[0].Text, result[0].Text);
        Assert.Empty(result[0].StyleSpans);
    }

    [Fact]
    public void Provider_TableOutputOverLimitFallsBackWithoutTruncation()
    {
        string[] cells = Enumerable.Range(0, 15)
            .Select(_ => new string('x', 4094))
            .Append(new string('x', 4090))
            .ToArray();
        string sourceText = $"|{string.Join('|', cells)}|";
        Assert.True(sourceText.Length <= Limit);

        string separator = $"|{string.Join('|', Enumerable.Repeat("---", cells.Length))}|";
        string body = $"|{string.Join('|', Enumerable.Repeat("x", cells.Length))}|";
        ScannedLine[] lines = Lines(sourceText, separator, body);
        var provider = new MarkdownViewerPresentationProvider();

        IReadOnlyList<PresentedLine> result = provider.Present(
            new ViewerPresentationContext(lines, [], [], 80));

        Assert.Equal(sourceText, result[0].Text);
        Assert.Equal(sourceText.Length, result[0].Text.Length);
        Assert.Empty(result[0].StyleSpans);
    }

    [Fact]
    public void SearchNavigation_LargeSourceLineCannotBypassInlineGuard()
    {
        string sourceText = "**target** " + new string('x', Limit);
        byte[] bytes = Encoding.UTF8.GetBytes(sourceText + "\n");
        var reader = new MemoryFileByteReader(bytes);
        var cache = new BlockCache(reader, blockSize: 4096, capacity: 32);
        var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
        var state = new LargeFileViewerState(cache, scanner);

        ViewerSearchMatch? match = ViewerSearchEngine.Find(
            reader,
            state,
            new ViewerSearchRequest(
                "target",
                CaseSensitive: true,
                WholeWords: false,
                UseRegex: false,
                SearchHex: false),
            searchBackward: false);

        Assert.NotNull(match);
        ScannedLine source = scanner
            .ReadLinesAsync(match.LineStartOffset, 1, bytes.Length)
            .GetAwaiter()
            .GetResult()
            .Lines
            .Single();

        PresentedLine result = state.Presentation.Present(
            ViewerPresentationMode.Auto,
            "search.md",
            scanner,
            [source],
            80).Single();

        Assert.Equal(sourceText, result.Text);
        Assert.Empty(result.StyleSpans);
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
