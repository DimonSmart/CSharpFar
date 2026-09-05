using CSharpFar.Console;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class TriStateMatrixFormRowTests
{
    [Fact]
    public void Matrix_ExposesValuesBySemanticIdentifiersAndNavigatesCells()
    {
        TriStateMatrixFormRow matrix = CreateMatrix();

        Assert.Equal(CheckState.Indeterminate, matrix.GetValue("owner", "read"));
        Assert.Equal(FormInputResultKind.ValueChanged, matrix.HandleKey(Key(ConsoleKey.Spacebar), new FormRowInputContext(true)).Kind);
        Assert.Equal(CheckState.Checked, matrix.GetValue("owner", "read"));

        matrix.HandleKey(Key(ConsoleKey.RightArrow), new FormRowInputContext(true));
        matrix.HandleKey(Key(ConsoleKey.DownArrow), new FormRowInputContext(true));
        matrix.HandleKey(Key(ConsoleKey.Spacebar), new FormRowInputContext(true));

        Assert.Equal(CheckState.Checked, matrix.GetValue("group", "write"));
        Assert.Equal(CheckState.Unchecked, matrix.GetValue("owner", "write"));
    }

    [Fact]
    public void Matrix_MeasuresLongLabelsWithoutConsumerSuppliedGeometry()
    {
        TriStateMatrixFormRow matrix = FormControls.TriStateMatrix(
            "permissions",
            [new("read", "Read"), new("write", "Write"), new("execute", "Execute")],
            [new("owner", "Very long owner", [CheckState.Checked, CheckState.Unchecked, CheckState.Indeterminate])]);
        var driver = new FakeConsoleDriver(40, 4);
        var screen = new ScreenRenderer(driver);

        UiTestRender.Render(screen, canvas => matrix.Render(new FormRowRenderContext(canvas, new Rect(0, 0, 40, matrix.Height), focused: true)));

        Assert.Contains("Very long owner", driver.GetRow(1), StringComparison.Ordinal);
        Assert.Contains("Execute", driver.GetRow(0), StringComparison.Ordinal);
        Assert.Equal(CheckState.Indeterminate, matrix.GetValue("owner", "execute"));
    }

    [Fact]
    public void DesiredWidth_UsesWidestRequiredWidthForEveryEqualWidthColumn()
    {
        TriStateMatrixFormRow matrix = FormControls.TriStateMatrix(
            [new("read", "Very long option界"), new("execute", "X")],
            [new("owner", "Very long owner", [CheckState.Checked, CheckState.Unchecked])]);

        int columnWidth = Math.Max(ConsoleTextMetrics.GetCellWidth("Very long option界"), ConsoleTextMetrics.GetCellWidth("[ ]"));
        Assert.Equal(ConsoleTextMetrics.GetCellWidth("Very long owner") + 1 + columnWidth * 2 + 1, matrix.DesiredWidth);
    }

    [Fact]
    public void Render_AtDesiredWidth_ShowsLongestColumnWithoutClipping()
    {
        TriStateMatrixFormRow matrix = FormControls.TriStateMatrix(
            [new("read", "Very long option界"), new("execute", "X")],
            [new("owner", "Owner", [CheckState.Checked, CheckState.Unchecked])]);
        var driver = new FakeConsoleDriver(matrix.DesiredWidth, matrix.Height);
        var screen = new ScreenRenderer(driver);

        UiTestRender.Render(screen, canvas => matrix.Render(new FormRowRenderContext(canvas, new Rect(0, 0, matrix.DesiredWidth, matrix.Height), focused: false)));

        Assert.Contains("Very long option界", driver.GetRow(0), StringComparison.Ordinal);
    }

    private static TriStateMatrixFormRow CreateMatrix() => FormControls.TriStateMatrix(
        "permissions",
        [new("read", "Read"), new("write", "Write")],
        [
            new("owner", "Owner", [CheckState.Indeterminate, CheckState.Unchecked]),
            new("group", "Group", [CheckState.Unchecked, CheckState.Unchecked]),
        ]);

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}
