using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

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

    private static TriStateMatrixFormRow CreateMatrix() => FormControls.TriStateMatrix(
        "permissions",
        [new("read", "Read"), new("write", "Write")],
        [
            new("owner", "Owner", [CheckState.Indeterminate, CheckState.Unchecked]),
            new("group", "Group", [CheckState.Unchecked, CheckState.Unchecked]),
        ]);

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}
