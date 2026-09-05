using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class ScrollableListRendererTests
{
    [Fact]
    public void Render_UsesEmptyTextAndNeverWritesOutsideContentBounds()
    {
        var canvas = new RecordingCanvas(20, 2);
        var options = new ScrollableListRenderOptions<string>(static item => item, "empty", CellStyle.Default, CellStyle.Default, CellStyle.Default);
        var state = new ScrollableListState<string>([]);
        ScrollableListFrame frame = ScrollableListFrame.Calculate(state, new Rect(2, 1, 3, 1), null, new VerticalScrollbarController());

        ScrollableListRenderer.Render(canvas, state, frame, options);

        Assert.Contains(canvas.Writes, write => write.Text == "emp" && write.X == 2 && write.Y == 1);
        Assert.All(canvas.Writes, write => Assert.InRange(write.X, 0, 4));
    }

    private sealed class RecordingCanvas(int width, int height) : IUiCanvas
    {
        public List<(int X, int Y, string Text)> Writes { get; } = [];
        public ConsoleSize Size { get; } = new(width, height);
        public void Write(int x, int y, string text, CellStyle style) => Writes.Add((x, y, text));
        public void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style) => Write(x, y, text.ToString(), style);
        public void WriteForced(int x, int y, string text, CellStyle style) => Write(x, y, text, style);
        public void WriteForced(int x, int y, ReadOnlySpan<char> text, CellStyle style) => Write(x, y, text, style);
        public void WriteChar(int x, int y, char ch, CellStyle style) => Write(x, y, ch.ToString(), style);
        public void FillRegion(Rect region, CellStyle style) { }
        public void DrawBox(Rect rect, CellStyle style) { }
        public void DrawDoubleBox(Rect rect, CellStyle style) { }
    }
}
