using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class CheckBoxColumnsRowTests
{
    [Fact]
    public void ResolveCurrent_DoesNotCommitFallbackPosition()
    {
        var navigation = new FormGridNavigationState();
        var shape = new FormGridShape([1, 1]);

        Assert.Equal(new FormGridPosition(1, 0), navigation.ResolveCurrent(shape, position => position.Column == 1));
        Assert.Null(navigation.Current);
    }

    [Fact]
    public void Height_UsesTallestColumn()
    {
        var row = new CheckBoxColumnsRow(
            [
                [Check("one"), Check("two"), Check("three")],
                [Check("four")],
            ]);

        Assert.Equal(3, row.Height);
    }

    [Fact]
    public void Render_DistributesBoundsBetweenColumns()
    {
        var canvas = new RecordingCanvas(20, 3);
        var row = new CheckBoxColumnsRow([[Check("A")], [Check("B")]], columnGap: 2);

        row.Render(new FormRowRenderContext(canvas, new Rect(0, 0, 12, 1), focused: false));

        Assert.Contains(canvas.Writes, write => write.X == 0 && write.Text == "[ ] A");
        Assert.Contains(canvas.Writes, write => write.X == 7 && write.Text == "[ ] B");
    }

    [Fact]
    public void Space_TogglesCurrentCheckBox()
    {
        CheckBoxRow first = Check("first");
        var row = new CheckBoxColumnsRow([[first], [Check("second")]]);

        FormInputResult result = row.HandleKey(Key(ConsoleKey.Spacebar), Input());

        Assert.Equal(FormInputResultKind.ValueChanged, result.Kind);
        Assert.True(first.Value);
    }

    [Fact]
    public void LeftRight_MoveInternalFocusBetweenColumns()
    {
        CheckBoxRow left = Check("left");
        CheckBoxRow right = Check("right");
        var row = new CheckBoxColumnsRow([[left], [right]]);

        row.HandleKey(Key(ConsoleKey.RightArrow), Input());
        row.HandleKey(Key(ConsoleKey.Spacebar), Input());
        row.HandleKey(Key(ConsoleKey.LeftArrow), Input());
        row.HandleKey(Key(ConsoleKey.Spacebar), Input());

        Assert.True(left.Value);
        Assert.True(right.Value);
    }

    [Fact]
    public void Tab_UsesVisualRowMajorOrderAndLeavesAtEdges()
    {
        CheckBoxRow leftTop = Check("left top");
        CheckBoxRow leftBottom = Check("left bottom");
        CheckBoxRow rightTop = Check("right top");
        CheckBoxRow rightBottom = Check("right bottom");
        var row = new CheckBoxColumnsRow([[leftTop, leftBottom], [rightTop, rightBottom]]);

        row.HandleKey(Key(ConsoleKey.Tab), Input());
        row.HandleKey(Key(ConsoleKey.Spacebar), Input());
        row.HandleKey(Key(ConsoleKey.Tab), Input());
        row.HandleKey(Key(ConsoleKey.Spacebar), Input());
        row.HandleKey(Key(ConsoleKey.Tab), Input());
        row.HandleKey(Key(ConsoleKey.Spacebar), Input());
        FormInputResult last = row.HandleKey(Key(ConsoleKey.Tab), Input());

        Assert.False(leftTop.Value);
        Assert.True(rightTop.Value);
        Assert.True(leftBottom.Value);
        Assert.True(rightBottom.Value);
        Assert.Equal(FormInputResultKind.MoveFocusNext, last.Kind);

        FormInputResult first = row.HandleKey(Key(ConsoleKey.Tab, shift: true), Input());
        Assert.Equal(FormInputResultKind.Handled, first.Kind);
        first = row.HandleKey(Key(ConsoleKey.Tab, shift: true), Input());
        first = row.HandleKey(Key(ConsoleKey.Tab, shift: true), Input());
        first = row.HandleKey(Key(ConsoleKey.Tab, shift: true), Input());
        Assert.Equal(FormInputResultKind.MoveFocusPrevious, first.Kind);
    }

    [Fact]
    public void Navigation_SkipsDisabledCheckBoxes()
    {
        CheckBoxRow disabled = Check("disabled");
        disabled.Enabled = false;
        CheckBoxRow enabled = Check("enabled");
        var row = new CheckBoxColumnsRow([[disabled], [enabled]]);

        row.HandleKey(Key(ConsoleKey.Spacebar), Input());

        Assert.False(disabled.Value);
        Assert.True(enabled.Value);
    }

    [Fact]
    public void MouseClick_TogglesTargetCellOnly()
    {
        CheckBoxRow left = Check("left");
        CheckBoxRow right = Check("right");
        var row = new CheckBoxColumnsRow([[left], [right]], columnGap: 2);
        var bounds = new Rect(0, 0, 12, 1);

        FormInputResult gapResult = row.HandleMouse(Mouse(6, 0), MouseContext(bounds));
        FormInputResult rightResult = row.HandleMouse(Mouse(7, 0), MouseContext(bounds));

        Assert.Equal(FormInputResultKind.NotHandled, gapResult.Kind);
        Assert.Equal(FormInputResultKind.ValueChanged, rightResult.Kind);
        Assert.False(left.Value);
        Assert.True(right.Value);
    }

    [Fact]
    public void MouseClick_EmptyShortColumnCellDoesNothing()
    {
        CheckBoxRow leftBottom = Check("left bottom");
        CheckBoxRow rightTop = Check("right top");
        var row = new CheckBoxColumnsRow([[Check("left top"), leftBottom], [rightTop]], columnGap: 2);
        var bounds = new Rect(0, 0, 20, 2);

        FormInputResult result = row.HandleMouse(Mouse(11, 1), MouseContext(bounds));

        Assert.Equal(FormInputResultKind.NotHandled, result.Kind);
        Assert.False(leftBottom.Value);
        Assert.False(rightTop.Value);
    }

    [Fact]
    public void DisabledCheckBoxRow_RendersDisabledStyleAndHidesCursor()
    {
        var canvas = new RecordingCanvas(20, 1);
        var row = Check("disabled");
        row.Enabled = false;

        row.Render(new FormRowRenderContext(canvas, new Rect(0, 0, 20, 1), focused: true));

        Assert.Contains(canvas.Writes, write =>
            write.Text.Contains("disabled", StringComparison.Ordinal) &&
            write.Style.Foreground == UiTheme.Current.DisabledControlForeground);
        Assert.False(row.TryGetCursor(new FormRowRenderContext(canvas, new Rect(0, 0, 20, 1), focused: true), out _));
    }

    private static CheckBoxRow Check(string label) => new(new CheckBoxLine(label));

    private static ConsoleKeyInfo Key(ConsoleKey key, bool shift = false) =>
        new('\0', key, shift, alt: false, control: false);

    private static MouseConsoleInputEvent Mouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private static FormRowInputContext Input() => new(true);

    private static FormRowMouseContext MouseContext(Rect bounds) => new(true, new FormRowLayout(bounds, null, bounds));

    private sealed class RecordingCanvas(int width, int height) : IUiCanvas
    {
        public List<WriteRecord> Writes { get; } = [];
        public ConsoleSize Size { get; } = new(width, height);

        public void Write(int x, int y, string text, CellStyle style) => Writes.Add(new WriteRecord(x, y, text, style));
        public void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style) => Write(x, y, text.ToString(), style);
        public void WriteForced(int x, int y, string text, CellStyle style) => Write(x, y, text, style);
        public void WriteForced(int x, int y, ReadOnlySpan<char> text, CellStyle style) => Write(x, y, text.ToString(), style);
        public void WriteChar(int x, int y, char ch, CellStyle style) => Write(x, y, ch.ToString(), style);
        public void FillRegion(Rect region, CellStyle style) { }
        public void DrawBox(Rect rect, CellStyle style) { }
        public void DrawDoubleBox(Rect rect, CellStyle style) { }
    }

    private readonly record struct WriteRecord(int X, int Y, string Text, CellStyle Style);
}
