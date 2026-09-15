from pathlib import Path


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one match in {path}, found {count}: {old[:100]!r}")
    write(path, text.replace(old, new, 1))


parser = r'''using System.Text;

namespace CSharpFar.App.Viewer;

internal sealed record MarkdownInlineTransform(
    string Text,
    IReadOnlyList<PresentedSourceSpan> SourceSpans,
    IReadOnlyList<PresentedStyleSpan> StyleSpans,
    bool Changed);

internal static class MarkdownInlinePresentation
{
    public static MarkdownInlineTransform Transform(string source, int sourceOffset = 0)
    {
        if (string.IsNullOrEmpty(source))
            return new MarkdownInlineTransform(source, [], [], false);

        var text = new StringBuilder(source.Length);
        var sourceSpans = new List<PresentedSourceSpan>();
        var styleSpans = new List<PresentedStyleSpan>();
        int rawStart = 0;
        int index = 0;
        bool changed = false;

        while (index < source.Length)
        {
            if (!TryReadConstruct(source, index, out var construct))
            {
                index++;
                continue;
            }

            AppendSourceRange(source, sourceOffset, rawStart, index - rawStart, text, sourceSpans);

            int presentedStart = text.Length;
            AppendSourceRange(
                source,
                sourceOffset,
                construct.ContentStart,
                construct.ContentLength,
                text,
                sourceSpans);
            styleSpans.Add(new PresentedStyleSpan(presentedStart, construct.ContentLength, construct.Style));

            changed = true;
            index = construct.EndExclusive;
            rawStart = index;
        }

        AppendSourceRange(source, sourceOffset, rawStart, source.Length - rawStart, text, sourceSpans);

        if (!changed)
        {
            return new MarkdownInlineTransform(
                source,
                source.Length == 0
                    ? []
                    : [new PresentedSourceSpan(sourceOffset, source.Length, 0, source.Length)],
                [],
                false);
        }

        return new MarkdownInlineTransform(text.ToString(), sourceSpans, styleSpans, true);
    }

    private static bool TryReadConstruct(string source, int index, out InlineConstruct construct)
    {
        construct = default;
        if (IsEscaped(source, index))
            return false;

        return source[index] switch
        {
            '`' => TryReadCode(source, index, out construct),
            '[' => TryReadLink(source, index, out construct),
            '*' => TryReadEmphasis(source, index, out construct),
            _ => false,
        };
    }

    private static bool TryReadCode(string source, int index, out InlineConstruct construct)
    {
        construct = default;
        int delimiterLength = CountRun(source, index, '`');
        int contentStart = index + delimiterLength;

        for (int current = contentStart; current < source.Length;)
        {
            if (source[current] != '`' || IsEscaped(source, current))
            {
                current++;
                continue;
            }

            int closingLength = CountRun(source, current, '`');
            if (closingLength == delimiterLength)
            {
                int contentLength = current - contentStart;
                if (contentLength <= 0)
                    return false;

                construct = new InlineConstruct(
                    contentStart,
                    contentLength,
                    current + closingLength,
                    ViewerTextStyle.InlineCode);
                return true;
            }

            current += closingLength;
        }

        return false;
    }

    private static bool TryReadEmphasis(string source, int index, out InlineConstruct construct)
    {
        construct = default;
        if (index > 0 && source[index - 1] == '*')
            return false;

        int delimiterLength = CountRun(source, index, '*');
        if (delimiterLength is not (1 or 2))
            return false;

        int contentStart = index + delimiterLength;
        for (int current = contentStart; current < source.Length;)
        {
            if (source[current] != '*' || IsEscaped(source, current))
            {
                current++;
                continue;
            }

            int closingLength = CountRun(source, current, '*');
            if (closingLength == delimiterLength)
            {
                int contentLength = current - contentStart;
                if (contentLength <= 0)
                    return false;

                construct = new InlineConstruct(
                    contentStart,
                    contentLength,
                    current + closingLength,
                    delimiterLength == 2 ? ViewerTextStyle.Bold : ViewerTextStyle.Italic);
                return true;
            }

            current += closingLength;
        }

        return false;
    }

    private static bool TryReadLink(string source, int index, out InlineConstruct construct)
    {
        construct = default;
        if (index > 0 && source[index - 1] == '!')
            return false;

        int labelStart = index + 1;
        int closeBracket = -1;
        for (int current = labelStart; current < source.Length; current++)
        {
            if (source[current] == '[' && !IsEscaped(source, current))
                return false;

            if (source[current] == ']' && !IsEscaped(source, current))
            {
                closeBracket = current;
                break;
            }
        }

        if (closeBracket <= labelStart ||
            closeBracket + 1 >= source.Length ||
            source[closeBracket + 1] != '(' ||
            IsEscaped(source, closeBracket + 1))
        {
            return false;
        }

        int destinationStart = closeBracket + 2;
        if (destinationStart >= source.Length)
            return false;

        int closeParenthesis = -1;
        for (int current = destinationStart; current < source.Length; current++)
        {
            if (source[current] == '(' && !IsEscaped(source, current))
                return false;

            if (source[current] == ')' && !IsEscaped(source, current))
            {
                closeParenthesis = current;
                break;
            }
        }

        if (closeParenthesis <= destinationStart)
            return false;

        construct = new InlineConstruct(
            labelStart,
            closeBracket - labelStart,
            closeParenthesis + 1,
            ViewerTextStyle.Link);
        return true;
    }

    private static void AppendSourceRange(
        string source,
        int sourceOffset,
        int sourceStart,
        int length,
        StringBuilder text,
        List<PresentedSourceSpan> sourceSpans)
    {
        if (length <= 0)
            return;

        int presentedStart = text.Length;
        text.Append(source, sourceStart, length);
        sourceSpans.Add(new PresentedSourceSpan(
            sourceOffset + sourceStart,
            length,
            presentedStart,
            length));
    }

    private static int CountRun(string source, int index, char value)
    {
        int length = 0;
        while (index + length < source.Length && source[index + length] == value)
            length++;
        return length;
    }

    private static bool IsEscaped(string source, int index)
    {
        int slashCount = 0;
        for (int current = index - 1; current >= 0 && source[current] == '\\'; current--)
            slashCount++;
        return slashCount % 2 != 0;
    }

    private readonly record struct InlineConstruct(
        int ContentStart,
        int ContentLength,
        int EndExclusive,
        ViewerTextStyle Style);
}
'''
write("src/CSharpFar.App/Viewer/MarkdownInlinePresentation.cs", parser)

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """internal enum ViewerColumnAlignment
{
    Left,
    Center,
    Right,
}

internal sealed record PresentedSourceSpan(
""",
    """internal enum ViewerColumnAlignment
{
    Left,
    Center,
    Right,
}

internal enum ViewerTextStyle
{
    Link,
    Bold,
    Italic,
    InlineCode,
}

internal sealed record PresentedSourceSpan(
""")

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """internal sealed record PresentedSourceSpan(
    int SourceStart,
    int SourceLength,
    int PresentedStart,
    int PresentedLength);

internal sealed record PresentedLine(
    ScannedLine Source,
    string Text,
    IReadOnlyList<PresentedSourceSpan> SourceSpans)
{
    public static PresentedLine Raw(ScannedLine source) =>
        new(
            source,
            source.Text,
            source.Text.Length == 0
                ? []
                : [new PresentedSourceSpan(0, source.Text.Length, 0, source.Text.Length)]);
""",
    """internal sealed record PresentedSourceSpan(
    int SourceStart,
    int SourceLength,
    int PresentedStart,
    int PresentedLength);

internal sealed record PresentedStyleSpan(
    int Start,
    int Length,
    ViewerTextStyle Style);

internal sealed record PresentedLine(
    ScannedLine Source,
    string Text,
    IReadOnlyList<PresentedSourceSpan> SourceSpans,
    IReadOnlyList<PresentedStyleSpan> StyleSpans)
{
    public PresentedLine(
        ScannedLine source,
        string text,
        IReadOnlyList<PresentedSourceSpan> sourceSpans)
        : this(source, text, sourceSpans, [])
    {
    }

    public static PresentedLine Raw(ScannedLine source) =>
        new(
            source,
            source.Text,
            source.Text.Length == 0
                ? []
                : [new PresentedSourceSpan(0, source.Text.Length, 0, source.Text.Length)],
            []);
""")

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """        var layout = FindForOffset(source.StartOffset);
        if (layout is null)
            return false;

        if (source.StartOffset == layout.SeparatorOffset)
""",
    """        var layout = FindForOffset(source.StartOffset);
        if (layout is null)
        {
            MarkdownInlineTransform inline = MarkdownInlinePresentation.Transform(source.Text);
            if (!inline.Changed)
                return false;

            presented = new PresentedLine(source, inline.Text, inline.SourceSpans, inline.StyleSpans);
            return true;
        }

        if (source.StartOffset == layout.SeparatorOffset)
""")

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """        for (int i = 0; i < cells.Count; i++)
        {
            int width = CellWidth(cells[i].Text);
            if (width > MaxColumnWidthCells)
                return false;

            layout.Widths[i] = Math.Max(layout.Widths[i], width);
        }
""",
    """        for (int i = 0; i < cells.Count; i++)
        {
            MarkdownInlineTransform inline = MarkdownInlinePresentation.Transform(cells[i].Text, cells[i].SourceStart);
            int width = CellWidth(inline.Text);
            if (width > MaxColumnWidthCells)
                return false;

            layout.Widths[i] = Math.Max(layout.Widths[i], width);
        }
""")

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """        var text = new StringBuilder();
        var spans = new List<PresentedSourceSpan>();
        text.Append('│');

        for (int i = 0; i < layout.ColumnCount; i++)
        {
            MarkdownCell cell = cells[i];
            int contentWidth = CellWidth(cell.Text);
            if (contentWidth > layout.Widths[i])
            {
                presented = PresentedLine.Raw(source);
                return false;
            }

            int padding = layout.Widths[i] - contentWidth;
            int leftPadding = layout.Alignments[i] switch
            {
                ViewerColumnAlignment.Right => padding,
                ViewerColumnAlignment.Center => padding / 2,
                _ => 0,
            };
            int rightPadding = padding - leftPadding;

            text.Append(' ');
            text.Append(' ', leftPadding);
            int presentedStart = text.Length;
            text.Append(cell.Text);
            if (cell.SourceLength > 0)
            {
                spans.Add(new PresentedSourceSpan(
                    cell.SourceStart,
                    cell.SourceLength,
                    presentedStart,
                    cell.Text.Length));
            }

            text.Append(' ', rightPadding);
            text.Append(' ');
            text.Append(i == layout.ColumnCount - 1 ? '│' : '│');

            if (text.Length > MaxPresentedLineChars)
            {
                presented = PresentedLine.Raw(source);
                return false;
            }
        }

        presented = new PresentedLine(source, text.ToString(), spans);
        return true;
""",
    """        var text = new StringBuilder();
        var sourceSpans = new List<PresentedSourceSpan>();
        var styleSpans = new List<PresentedStyleSpan>();
        text.Append('│');

        for (int i = 0; i < layout.ColumnCount; i++)
        {
            MarkdownCell cell = cells[i];
            MarkdownInlineTransform inline = MarkdownInlinePresentation.Transform(cell.Text, cell.SourceStart);
            int contentWidth = CellWidth(inline.Text);
            if (contentWidth > layout.Widths[i])
            {
                presented = PresentedLine.Raw(source);
                return false;
            }

            int padding = layout.Widths[i] - contentWidth;
            int leftPadding = layout.Alignments[i] switch
            {
                ViewerColumnAlignment.Right => padding,
                ViewerColumnAlignment.Center => padding / 2,
                _ => 0,
            };
            int rightPadding = padding - leftPadding;

            text.Append(' ');
            text.Append(' ', leftPadding);
            int presentedStart = text.Length;
            text.Append(inline.Text);

            foreach (PresentedSourceSpan span in inline.SourceSpans)
            {
                sourceSpans.Add(span with
                {
                    PresentedStart = span.PresentedStart + presentedStart,
                });
            }

            foreach (PresentedStyleSpan span in inline.StyleSpans)
            {
                styleSpans.Add(span with
                {
                    Start = span.Start + presentedStart,
                });
            }

            text.Append(' ', rightPadding);
            text.Append(' ');
            text.Append('│');

            if (text.Length > MaxPresentedLineChars)
            {
                presented = PresentedLine.Raw(source);
                return false;
            }
        }

        presented = new PresentedLine(source, text.ToString(), sourceSpans, styleSpans);
        return true;
""")

replace_once(
    "src/CSharpFar.App/CSharpFarPalette.cs",
    """    public ConsoleColor CommandLineFg { get; init; } = ConsoleColor.White;
    public ConsoleColor CommandLineBg { get; init; } = ConsoleColor.Black;
""",
    """    public ConsoleColor CommandLineFg { get; init; } = ConsoleColor.White;
    public ConsoleColor CommandLineBg { get; init; } = ConsoleColor.Black;
    public ConsoleColor MarkdownLinkFg { get; init; } = ConsoleColor.Cyan;
    public ConsoleColor MarkdownBoldFg { get; init; } = ConsoleColor.White;
    public ConsoleColor MarkdownItalicFg { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor MarkdownInlineCodeFg { get; init; } = ConsoleColor.Yellow;
""")

replace_once(
    "src/CSharpFar.App/CSharpFarPaletteStyles.cs",
    """    public static CellStyle CommandLine(CSharpFarPalette p) => new(p.CommandLineFg, p.CommandLineBg);
""",
    """    public static CellStyle CommandLine(CSharpFarPalette p) => new(p.CommandLineFg, p.CommandLineBg);
    public static CellStyle MarkdownLink(CSharpFarPalette p) => new(p.MarkdownLinkFg, p.CommandLineBg);
    public static CellStyle MarkdownBold(CSharpFarPalette p) => new(p.MarkdownBoldFg, p.CommandLineBg, TextAttributes.Bold);
    public static CellStyle MarkdownItalic(CSharpFarPalette p) => new(p.MarkdownItalicFg, p.CommandLineBg);
    public static CellStyle MarkdownInlineCode(CSharpFarPalette p) => new(p.MarkdownInlineCodeFg, p.CommandLineBg);
""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """        var layout = new ViewerTextLayout(line);
        string visible = layout.Slice(scrollLeft, width);
        canvas.WriteForced(0, y, visible, CSharpFarPaletteStyles.CommandLine(_palette));
        if (match is not { IsHex: false } ||
""",
    """        var layout = new ViewerTextLayout(line);
        string visible = layout.Slice(scrollLeft, width);
        canvas.WriteForced(0, y, visible, CSharpFarPaletteStyles.CommandLine(_palette));
        ApplyMarkdownStyles(canvas, presented, line, y, scrollLeft, width, segmentStartIndex, layout);
        if (match is not { IsHex: false } ||
""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """    private static PresentedLine ResolvePresentationForSearch(
""",
    """    private void ApplyMarkdownStyles(
        IUiCanvas canvas,
        PresentedLine presented,
        string line,
        int y,
        int scrollLeft,
        int width,
        int segmentStartIndex,
        ViewerTextLayout layout)
    {
        if (presented.StyleSpans.Count == 0)
            return;

        int segmentEndIndex = segmentStartIndex + line.Length;
        int visibleStart = scrollLeft;
        int visibleEnd = scrollLeft + width;

        foreach (PresentedStyleSpan span in presented.StyleSpans)
        {
            int spanEnd = span.Start + span.Length;
            if (spanEnd <= segmentStartIndex || span.Start >= segmentEndIndex)
                continue;

            int localStart = Math.Max(span.Start, segmentStartIndex) - segmentStartIndex;
            int localEnd = Math.Min(spanEnd, segmentEndIndex) - segmentStartIndex;
            int styleStartCell = layout.CellOffsetFromSourceIndex(localStart);
            int styleEndCell = layout.CellOffsetFromSourceIndex(localEnd);
            int drawStart = Math.Max(styleStartCell, visibleStart);
            int drawEnd = Math.Min(styleEndCell, visibleEnd);
            if (drawEnd <= drawStart)
                continue;

            string styled = layout.Slice(drawStart, drawEnd - drawStart);
            if (ConsoleTextMetrics.GetCellWidth(styled) > 0)
                canvas.Write(drawStart - visibleStart, y, styled, ResolveMarkdownStyle(span.Style));
        }
    }

    private CellStyle ResolveMarkdownStyle(ViewerTextStyle style) =>
        style switch
        {
            ViewerTextStyle.Link => CSharpFarPaletteStyles.MarkdownLink(_palette),
            ViewerTextStyle.Bold => CSharpFarPaletteStyles.MarkdownBold(_palette),
            ViewerTextStyle.Italic => CSharpFarPaletteStyles.MarkdownItalic(_palette),
            ViewerTextStyle.InlineCode => CSharpFarPaletteStyles.MarkdownInlineCode(_palette),
            _ => CSharpFarPaletteStyles.CommandLine(_palette),
        };

    private static PresentedLine ResolvePresentationForSearch(
""")

unit_tests = r'''using System.Text;
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
        ViewerTextStyle style)
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(source);

        Assert.Equal(expected, result.Text);
        PresentedStyleSpan span = Assert.Single(result.StyleSpans);
        Assert.Equal(new PresentedStyleSpan(0, expected.Length, style), span);
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
'''
write("tests/CSharpFar.Tests/MarkdownInlinePresentationTests.cs", unit_tests)

replace_once(
    "tests/CSharpFar.Tests/ViewerPresentationIntegrationTests.cs",
    """    private static FakeConsoleDriver ViewerDriver(int width = 80, int height = 10) =>
""",
    r'''    [Fact]
    public void Show_InlineMarkdownRendersSemanticStyles()
    {
        string path = Write("inline.md", "plain **bold** `code` [link](url) *italic*\n");
        var driver = ViewerDriver(width: 100);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string row = driver.GetRegionText(new Rect(0, 1, 100, 1));
        Assert.StartsWith("plain bold code link italic", row);
        Assert.DoesNotContain("**", row);
        Assert.DoesNotContain("(url)", row);

        int boldX = row.IndexOf("bold", StringComparison.Ordinal);
        int codeX = row.IndexOf("code", StringComparison.Ordinal);
        int linkX = row.IndexOf("link", StringComparison.Ordinal);
        int italicX = row.IndexOf("italic", StringComparison.Ordinal);
        Assert.Equal(CSharpFarPalette.Default.MarkdownBoldFg, driver.GetCell(boldX, 1).Foreground);
        Assert.True((driver.GetCell(boldX, 1).Attributes & TextAttributes.Bold) != 0);
        Assert.Equal(CSharpFarPalette.Default.MarkdownInlineCodeFg, driver.GetCell(codeX, 1).Foreground);
        Assert.Equal(CSharpFarPalette.Default.MarkdownLinkFg, driver.GetCell(linkX, 1).Foreground);
        Assert.Equal(CSharpFarPalette.Default.MarkdownItalicFg, driver.GetCell(italicX, 1).Foreground);
    }

    [Fact]
    public void Show_SearchHighlightOverridesMarkdownStyle()
    {
        string path = Write("search-inline.md", "See **target** here.\n");
        var driver = ViewerDriver(width: 60, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.F7));
        EnqueueText(driver, "target");
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string row = driver.GetRegionText(new Rect(0, 1, 60, 1));
        int targetX = row.IndexOf("target", StringComparison.Ordinal);
        Assert.True(targetX >= 0, row);
        var cell = driver.GetCell(targetX, 1);
        CellStyle searchStyle = CSharpFarPaletteStyles.InputHighlight(CSharpFarPalette.Default);
        Assert.Equal(searchStyle.Foreground, cell.Foreground);
        Assert.Equal(searchStyle.Background, cell.Background);
    }

    [Fact]
    public void Show_SearchHiddenLinkDestinationFallsBackToRaw()
    {
        string path = Write("search-hidden.md", "See [documentation](README.md).\n");
        var driver = ViewerDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.F7));
        EnqueueText(driver, "README.md");
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string row = driver.GetRegionText(new Rect(0, 1, 80, 1));
        Assert.Contains("[documentation](README.md)", row);
    }

    [Fact]
    public void Show_F5RawRestoresInlineMarkdownMarkers()
    {
        string path = Write("inline-raw.md", "**bold** `code` [link](url) *italic*\n");
        var driver = ViewerDriver(width: 100);
        driver.EnqueueKey(Key(ConsoleKey.F5));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string row = driver.GetRegionText(new Rect(0, 1, 100, 1));
        Assert.Contains("**bold** `code` [link](url) *italic*", row);
    }

    [Fact]
    public void Show_WideStyledCharacterSurvivesHorizontalScrolling()
    {
        string path = Write("wide-inline.md", "0123456789 **界** tail\n");
        var driver = ViewerDriver(width: 12);
        for (int i = 0; i < 5; i++)
            driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        UiTestCanvas.FileViewerFor(new ScreenRenderer(driver)).Show(path);

        string row = driver.GetRegionText(new Rect(0, 1, 12, 1));
        Assert.Contains("界", row);
    }

    private static FakeConsoleDriver ViewerDriver(int width = 80, int height = 10) =>
''')

# The workflow removes this patch helper before creating the final commit.
