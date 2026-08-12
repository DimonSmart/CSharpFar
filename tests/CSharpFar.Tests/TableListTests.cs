using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TableListTests
{
    [Fact]
    public void Render_UsesColumnsSeparatorsAlignmentUnicodeAndSelectionWithoutTrailingSeparator()
    {
        var state = new ScrollableListState<Item>([new("表long", "42")], selectedIndex: 0);
        var table = CreateTable(state);
        var driver = new FakeConsoleDriver(20, 4);
        var normal = new CellStyle(ConsoleColor.White, ConsoleColor.Black);
        var selected = new CellStyle(ConsoleColor.Black, ConsoleColor.White);
        var emphasized = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black);
        var emphasizedSelected = new CellStyle(ConsoleColor.Yellow, ConsoleColor.White);
        ScrollableListFrame frame = table.List.CalculateFrame(new Rect(0, 2, 20, 1), null);

        UiTestRender.Render(new ScreenRenderer(driver), canvas => table.Render(
            canvas, frame, new Rect(0, 0, 20, 2), normal, normal, normal, selected, emphasized, emphasizedSelected));

        Assert.Equal("Name │ Size         ", driver.GetRow(0));
        Assert.Equal("─────┼─────         ", driver.GetRow(1));
        Assert.Equal("…ong │   42         ", driver.GetRow(2));
        Assert.Equal(ConsoleColor.White, driver.GetCell(0, 2).Background);
        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(9, 2).Foreground);
        Assert.Equal(ConsoleColor.White, driver.GetCell(9, 2).Background);
    }

    [Fact]
    public void Render_NarrowViewportClipsAtColumnBoundaryAndWideViewportFillsWithoutTrailingSeparator()
    {
        var table = CreateTable(new ScrollableListState<Item>([new("Alpha", "7")]));
        var narrow = new FakeConsoleDriver(6, 3);
        var wide = new FakeConsoleDriver(24, 3);
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.Black);

        UiTestRender.Render(new ScreenRenderer(narrow), canvas => table.Render(
            canvas, table.List.CalculateFrame(new Rect(0, 2, 6, 1), null), new Rect(0, 0, 6, 2), style, style, style, style, style, style));
        UiTestRender.Render(new ScreenRenderer(wide), canvas => table.Render(
            canvas, table.List.CalculateFrame(new Rect(0, 2, 24, 1), null), new Rect(0, 0, 24, 2), style, style, style, style, style, style));

        Assert.Equal("Name │", narrow.GetRow(0));
        Assert.Equal("Name │ Size             ", wide.GetRow(0));
        Assert.DoesNotContain('│', wide.GetRow(0).Skip(11));
        Assert.DoesNotContain('│', wide.GetRow(2).Skip(11));
    }

    private static TableList<Item> CreateTable(ScrollableListState<Item> state) => new(
        new TableListDefinition<Item>
        {
            Columns =
            [
                TableColumn<Item>.Text("Name", item => item.Name, 4),
                TableColumn<Item>.Text("Size", item => item.Size, 4, TableColumnAlignment.Right, emphasized: true),
            ],
        },
        state,
        new UiTargetId("table"),
        new UiTargetId("table.scrollbar"));

    private sealed record Item(string Name, string Size);
}
