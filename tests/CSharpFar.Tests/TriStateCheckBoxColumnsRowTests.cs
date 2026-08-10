using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TriStateCheckBoxColumnsRowTests
{
    [Fact]
    public void DesiredWidth_UsesWidestCheckboxColumnForEveryEqualWidthColumn()
    {
        var row = CreateRow();

        int columnWidth = ConsoleTextMetrics.GetCellWidth("[ ] Very long option界");
        Assert.Equal(ConsoleTextMetrics.GetCellWidth("Options") + columnWidth * 2 + 1, row.DesiredWidth);
    }

    [Fact]
    public void Render_AtDesiredWidth_ShowsLongestColumnWithoutClipping()
    {
        var row = CreateRow();
        var driver = new FakeConsoleDriver(row.DesiredWidth, 1);
        var screen = new ScreenRenderer(driver);

        UiTestRender.Render(screen, canvas => row.Render(new FormRowRenderContext(canvas, new Rect(0, 0, row.DesiredWidth, 1), focused: false)));

        Assert.Contains("Very long option界", driver.GetRow(0), StringComparison.Ordinal);
    }

    private static TriStateCheckBoxColumnsRow CreateRow() => new(
        "Options",
        [new TriStateCheckBoxLine("Very long option界"), new TriStateCheckBoxLine("X")],
        labelWidth: ConsoleTextMetrics.GetCellWidth("Options"));
}
