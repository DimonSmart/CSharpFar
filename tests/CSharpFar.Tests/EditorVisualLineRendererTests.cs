using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class EditorVisualLineRendererTests
{
    private static readonly CellStyle TextStyle = new(ConsoleColor.Gray, ConsoleColor.Black);
    private static readonly CellStyle SelectionStyle = new(ConsoleColor.Black, ConsoleColor.White);

    [Fact]
    public void Render_ExpandsTabsAndWritesOneContiguousPlainRun()
    {
        var canvas = new RecordingCanvas();

        Render(canvas, "a\tb", width: 7, tabSize: 4);

        Write write = Assert.Single(canvas.Writes);
        Assert.Equal(0, write.X);
        Assert.Equal("a   b  ", write.Text);
        Assert.Equal(TextStyle, write.Style);
    }

    [Fact]
    public void Render_PreservesUnicodeScalarsAndCombiningMarks()
    {
        var canvas = new RecordingCanvas();

        Render(canvas, "Ж🙂界e\u0301", width: 8);

        Write write = Assert.Single(canvas.Writes);
        Assert.Equal("Ж🙂界e\u0301  ", write.Text);
    }

    [Fact]
    public void Render_ClipsInsideWideGlyphWithoutWritingItsHalf()
    {
        var canvas = new RecordingCanvas();

        Render(canvas, "A🙂B", width: 3, leftColumn: 2);

        Write write = Assert.Single(canvas.Writes);
        Assert.Equal(" B ", write.Text);
    }

    [Fact]
    public void Render_ConsumesSortedSyntaxSpansAndSelectionOverridesSyntax()
    {
        var canvas = new RecordingCanvas();
        var syntax = new[]
        {
            new EditorColorSpan(0, 0, 1, new CellStyle(ConsoleColor.Red, ConsoleColor.Black)),
            new EditorColorSpan(0, 1, 2, new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black)),
        };

        Render(
            canvas,
            "abcd",
            width: 4,
            syntax: syntax,
            selection: new EditorSelection(new EditorPosition(0, 1), new EditorPosition(0, 3), EditorSelectionMode.Linear));

        Assert.Collection(
            canvas.Writes,
            write => Assert.Equal(("a", new CellStyle(ConsoleColor.Red, ConsoleColor.Black)), (write.Text, write.Style)),
            write => Assert.Equal(("bc", SelectionStyle), (write.Text, write.Style)),
            write => Assert.Equal(("d", TextStyle), (write.Text, write.Style)));
    }

    [Fact]
    public void Render_CustomWideCursorOverridesSyntaxButNormalCursorDoesNot()
    {
        var canvas = new RecordingCanvas();
        var syntax = new[] { new EditorColorSpan(0, 1, 2, new CellStyle(ConsoleColor.Cyan, ConsoleColor.Black)) };

        Render(canvas, "a🙂b", width: 4, syntax: syntax, cursorColumn: 1, customCursorVisible: true);

        Assert.Contains(canvas.Writes, write => write.Text == "🙂" && write.Style.Equals(SelectionStyle));
    }

    [Fact]
    public void Render_DeterministicMixedUnicodeAndTabCasesMatchCellReference()
    {
        var random = new Random(0x5EED);
        string[] tokens = ["a", "Z", " ", "\t", "Ж", "界", "🙂"];

        for (int sample = 0; sample < 200; sample++)
        {
            string line = string.Concat(Enumerable.Range(0, random.Next(1, 40)).Select(_ => tokens[random.Next(tokens.Length)]));
            int width = random.Next(1, 31);
            int leftColumn = random.Next(0, 25);
            int tabSize = random.Next(2) == 0 ? 4 : 8;
            var canvas = new RecordingCanvas();

            Render(canvas, line, width, leftColumn, tabSize);

            Assert.Equal(RenderCellReference(line, width, leftColumn, tabSize), string.Concat(canvas.Writes.Select(write => write.Text)));
        }
    }

    private static string RenderCellReference(string line, int width, int leftColumn, int tabSize)
    {
        var result = new StringBuilder(width);
        int visual = 0;
        int outputCells = 0;
        foreach (Rune rune in line.EnumerateRunes())
        {
            int cellWidth = rune.Value == '\t' ? tabSize - visual % tabSize : ConsoleTextMetrics.GetCellWidth(rune);
            if (visual + cellWidth <= leftColumn)
            {
                visual += cellWidth;
                continue;
            }

            int clippedLeft = Math.Max(0, leftColumn - visual);
            int visibleWidth = Math.Min(cellWidth - clippedLeft, width - outputCells);
            if (visibleWidth <= 0)
                break;

            result.Append(clippedLeft == 0 && visibleWidth == cellWidth && rune.Value != '\t'
                ? rune.ToString()
                : new string(' ', visibleWidth));
            outputCells += visibleWidth;
            visual += cellWidth;
        }

        int remaining = width - outputCells;
        if (remaining > 0)
            result.Append(' ', remaining);
        return result.ToString();
    }

    private static void Render(
        RecordingCanvas canvas,
        string line,
        int width,
        int leftColumn = 0,
        int tabSize = 4,
        IReadOnlyList<EditorColorSpan>? syntax = null,
        EditorSelection? selection = null,
        int cursorColumn = -1,
        bool customCursorVisible = false) =>
        EditorVisualLineRenderer.Render(
            canvas, 0, 0, width, line, 0, leftColumn, tabSize, syntax ?? [], selection,
            0, cursorColumn, customCursorVisible, TextStyle, SelectionStyle);

    private readonly record struct Write(int X, string Text, CellStyle Style);

    private sealed class RecordingCanvas : IUiCanvas
    {
        public List<Write> Writes { get; } = [];
        public ConsoleSize Size => new(200, 20);

        public void Write(int x, int y, string text, CellStyle style) => Writes.Add(new Write(x, text, style));
        public void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style) => Write(x, y, text.ToString(), style);
        public void WriteForced(int x, int y, string text, CellStyle style) => Write(x, y, text, style);
        public void WriteForced(int x, int y, ReadOnlySpan<char> text, CellStyle style) => Write(x, y, text, style);
        public void WriteChar(int x, int y, char ch, CellStyle style) => Write(x, y, ch.ToString(), style);
        public void FillRegion(Rect region, CellStyle style) { }
        public void DrawBox(Rect rect, CellStyle style) { }
        public void DrawDoubleBox(Rect rect, CellStyle style) { }
    }
}
