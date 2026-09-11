using CSharpFar.App.Viewer;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public class ViewerPresentationTests
{
    [Fact]
    public void Registry_SelectsMarkdownByDisplayPathOnly()
    {
        Assert.IsType<MarkdownViewerPresentationProvider>(ViewerPresentationRegistry.Create("virtual://docs/README.MD?rev=1"));
        Assert.IsType<MarkdownViewerPresentationProvider>(ViewerPresentationRegistry.Create("notes.markdown#section"));
        Assert.Null(ViewerPresentationRegistry.Create("notes.txt"));
    }

    [Fact]
    public void MarkdownProvider_LeavesOrdinaryTextRaw()
    {
        ScannedLine source = Line(0, "ordinary text");

        PresentedLine result = Present(source).Single();

        Assert.Equal(source.Text, result.Text);
        Assert.Equal(source, result.Source);
    }

    [Fact]
    public void MarkdownProvider_RendersSimpleTableWithoutSyntheticRows()
    {
        ScannedLine[] source = Lines(
            "| Name | Type | Description |",
            "| --- | :---: | ---: |",
            "| Foo | spec | Some text |");

        IReadOnlyList<PresentedLine> result = Present(source);

        Assert.Equal(source.Length, result.Count);
        Assert.Equal("│ Name │ Type │ Description │", result[0].Text);
        Assert.Equal("├──────┼──────┼─────────────┤", result[1].Text);
        Assert.Equal("│ Foo  │ spec │   Some text │", result[2].Text);
    }

    [Fact]
    public void MarkdownProvider_SupportsOptionalOuterPipes()
    {
        ScannedLine[] source = Lines(
            "Name | Type",
            "--- | ---:",
            "Foo | spec");

        IReadOnlyList<PresentedLine> result = Present(source);

        Assert.Equal("│ Name │ Type │", result[0].Text);
        Assert.Equal("│ Foo  │ spec │", result[2].Text);
    }

    [Fact]
    public void MarkdownParser_PreservesEmptyCellsAndDoesNotSplitEscapedOrCodePipes()
    {
        bool parsed = MarkdownViewerPresentationProvider.TryParseRow(
            @"| A | | left\|right | `x|y` |",
            out var cells);

        Assert.True(parsed);
        Assert.Equal(4, cells.Count);
        Assert.Equal("A", cells[0].Text);
        Assert.Equal(string.Empty, cells[1].Text);
        Assert.Equal(@"left\|right", cells[2].Text);
        Assert.Equal("`x|y`", cells[3].Text);
    }

    [Fact]
    public void MarkdownSeparator_RecognizesLeftCenterRightAlignment()
    {
        bool parsed = MarkdownViewerPresentationProvider.TryParseSeparator(
            "| :--- | :---: | ---: |",
            out var alignments);

        Assert.True(parsed);
        Assert.Equal(
            [ViewerColumnAlignment.Left, ViewerColumnAlignment.Center, ViewerColumnAlignment.Right],
            alignments);
    }

    [Theory]
    [InlineData("| -- | --- |")]
    [InlineData("| :--: | --- |")]
    [InlineData("| ---x | --- |")]
    public void MarkdownProvider_MalformedSeparatorFallsBackToRaw(string separator)
    {
        ScannedLine[] source = Lines("| A | B |", separator, "| C | D |");

        IReadOnlyList<PresentedLine> result = Present(source);

        Assert.Equal(source.Select(line => line.Text), result.Select(line => line.Text));
    }

    [Fact]
    public void MarkdownProvider_UsesTerminalCellWidthForWideUnicode()
    {
        ScannedLine[] source = Lines(
            "| 界 | X |",
            "| --- | --- |",
            "| a | YY |");

        IReadOnlyList<PresentedLine> result = Present(source);

        Assert.Equal(
            ConsoleTextMetrics.GetCellWidth(result[0].Text),
            ConsoleTextMetrics.GetCellWidth(result[2].Text));
    }

    [Fact]
    public void MarkdownProvider_MapsDisplayedCellTextBackToSource()
    {
        ScannedLine[] source = Lines(
            "| Name | Type |",
            "| --- | --- |",
            "| Foo | spec |");

        PresentedLine body = Present(source)[2];
        int sourceStart = source[2].Text.IndexOf("Foo", StringComparison.Ordinal);

        Assert.True(body.TryMapSourceRange(sourceStart, 3, out int presentedStart, out int presentedLength));
        Assert.Equal("Foo", body.Text.Substring(presentedStart, presentedLength));
    }

    [Fact]
    public void MarkdownProvider_SeparatorDecorationHasNoSourceMapping()
    {
        ScannedLine[] source = Lines("| A | B |", "| --- | --- |", "| C | D |");

        PresentedLine separator = Present(source)[1];

        Assert.Empty(separator.SourceSpans);
        Assert.False(separator.TryMapSourceRange(0, 1, out _, out _));
    }

    [Fact]
    public void MarkdownProvider_PadsShortBodyRows()
    {
        ScannedLine[] source = Lines(
            "| A | B | C |",
            "| --- | --- | --- |",
            "| one | two |");

        PresentedLine body = Present(source)[2];

        Assert.StartsWith("│ one │ two │", body.Text);
        Assert.EndsWith("│", body.Text);
    }

    [Fact]
    public void MarkdownProvider_DoesNotConsumePlainTextAfterTable()
    {
        ScannedLine[] source = Lines(
            "| A | B |",
            "| --- | --- |",
            "| C | D |",
            "plain paragraph after table");

        IReadOnlyList<PresentedLine> result = Present(source);

        Assert.StartsWith("│", result[2].Text);
        Assert.Equal(source[3].Text, result[3].Text);
    }

    [Fact]
    public void MarkdownProvider_OversizedCellFallsBackToRaw()
    {
        string oversized = new('x', MarkdownViewerPresentationProvider.MaxColumnWidthCells + 1);
        ScannedLine[] source = Lines(
            "| A | B |",
            "| --- | --- |",
            $"| {oversized} | ok |");

        IReadOnlyList<PresentedLine> result = Present(source);

        Assert.Equal(source[2].Text, result[2].Text);
    }

    [Fact]
    public void MarkdownProvider_CanRecoverTableFromBoundedBeforeContext()
    {
        ScannedLine[] all = Lines(
            "| A | B |",
            "| --- | ---: |",
            "| one | 1 |",
            "| two | 22 |");
        var provider = new MarkdownViewerPresentationProvider();
        var context = new ViewerPresentationContext(
            [all[3]],
            [all[0], all[1], all[2]],
            [],
            80);

        PresentedLine result = provider.Present(context).Single();

        Assert.StartsWith("│ two", result.Text);
        Assert.EndsWith("22 │", result.Text);
    }

    [Fact]
    public void PresentedLine_RawMappingKeepsSourceCoordinates()
    {
        ScannedLine source = Line(100, "alpha beta");
        PresentedLine line = PresentedLine.Raw(source);

        Assert.True(line.TryMapSourceRange(6, 4, out int start, out int length));
        Assert.Equal(6, start);
        Assert.Equal(4, length);
    }

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
