using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TableListTests
{
    [Fact]
    public void Widths_RejectNegativeFixedAndInvalidMinimum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TableWidth.Fixed(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TableWidth.Flexible(2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => TableWidth.Optional(2, -1));
    }

    [Fact]
    public void Layout_ShrinksOptionalBeforeCollapsingInPriorityOrder()
    {
        var table = new TableList<Item>([new("a", "b", "c")], Definition(
            TableColumn<Item>.Text("Required", x => x.First, TableWidth.Fixed(4)),
            TableColumn<Item>.Text("First", x => x.Second, TableWidth.Optional(6, 2, priority: 0)),
            TableColumn<Item>.Text("Second", x => x.Third, TableWidth.Optional(6, 2, priority: 1))));

        TableListFrame shrunk = table.CalculateFrame(new Rect(0, 0, 17, 3));
        TableListFrame collapsed = table.CalculateFrame(new Rect(0, 0, 12, 3));

        Assert.Equal([4, 5, 2], shrunk.Columns.Select(x => x.Width));
        Assert.Equal(["Required", "Second"], collapsed.Columns.Select(x => x.Header));
    }

    [Fact]
    public void Layout_WideNarrowWideRestoresPreferredGeometryAndReservesScrollbar()
    {
        var table = CreateTable(Enumerable.Range(0, 8).Select(x => new Item($"Name {x}", x.ToString(), string.Empty)).ToArray());
        TableListFrame wide = table.CalculateFrame(new Rect(0, 0, 30, 5));
        _ = table.CalculateFrame(new Rect(0, 0, 8, 5));
        TableListFrame restored = table.CalculateFrame(new Rect(0, 0, 30, 5));

        Assert.True(wide.HasScrollbar);
        Assert.Equal(wide.Columns.Select(x => x.Width), restored.Columns.Select(x => x.Width));
        Assert.Equal(29, wide.BodyBounds.Right);
    }

    [Fact]
    public void Render_DuplicateHeadersUseTheirOwnDefinitionsAndAlignment()
    {
        var table = new TableList<Item>([new("left", "42", string.Empty)], Definition(
            TableColumn<Item>.Text("Value", x => x.First, 4),
            TableColumn<Item>.Text("Value", x => x.Second, 4, TableColumnAlignment.Right)));
        var driver = new FakeConsoleDriver(12, 3);
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 12, 3));

        UiTestRender.Render(new ScreenRenderer(driver), canvas => table.Render(canvas, frame));

        Assert.Equal("left │   42", driver.GetRow(2).TrimEnd());
    }

    [Fact]
    public void Render_MenuAppearanceUsesMenuStylesForNormalSelectedAndEmphasis()
    {
        var table = new TableList<Item>([new("x", "y", string.Empty)], Definition(TableColumn<Item>.Text("Name", x => x.First, 4, emphasized: true), TableColumn<Item>.Text("Size", x => x.Second, 4)), appearance: ListAppearance.Menu);
        var driver = new FakeConsoleDriver(12, 3);
        UiTestRender.Render(new ScreenRenderer(driver), canvas => table.Render(canvas, table.CalculateFrame(new Rect(0, 0, 12, 3))));

        Assert.Equal(UiTheme.Current.MenuActiveHighlightBg, driver.GetCell(0, 2).Background);
        Assert.Equal(UiTheme.Current.MenuActiveHighlightFg, driver.GetCell(0, 2).Foreground);
        Assert.Equal(UiTheme.Current.MenuActiveBg, driver.GetCell(7, 2).Background);
    }

    [Fact]
    public void State_PreservesSelectionByIdentityAndRoutesScrollbar()
    {
        var table = CreateTable(Enumerable.Range(0, 8).Select(x => new Item($"Name {x}", x.ToString(), string.Empty)).ToArray(), 4);
        table.ReplaceItems(Enumerable.Range(3, 8).Select(x => new Item($"Name {x}", x.ToString(), string.Empty)).ToArray(), x => x.First);
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 20, 5));
        UiInteractionFragment fragment = table.BuildInteractionFragment(frame);
        UiHitRegion scrollbar = Assert.Single(fragment.HitRegions, x => x.Bounds.Width == 1);
        var route = UiInputRouteContext.HitTarget(new UiFocusController(), scrollbar.Target);

        (ScrollableListInputResult result, _) = table.RouteInput(new MouseConsoleInputEvent(scrollbar.Bounds.X, scrollbar.Bounds.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None), frame, route);

        Assert.Equal("Name 4", table.SelectedItem!.First);
        Assert.True(result.IsHandled);
    }

    [Fact]
    public void SectionBreak_RendersWithVisibleColumnGeometryAndDoesNotChangeLogicalSelection()
    {
        var table = new TableList<Item>([new("volume", "1", "x"), new("module", "2", "y")], new TableListDefinition<Item>
        {
            Columns =
            [
                TableColumn<Item>.Text("Name", item => item.First, 6),
                TableColumn<Item>.Text("Size", item => item.Second, TableWidth.Optional(4, 0)),
            ],
            SectionBreakBetween = static (previous, current) => previous.First != current.First,
        });
        var driver = new FakeConsoleDriver(7, 5);
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 7, 5));

        UiTestRender.Render(new ScreenRenderer(driver), canvas => table.Render(canvas, frame));

        Assert.StartsWith("──────", driver.GetRow(3), StringComparison.Ordinal);
        Assert.Equal(0, table.SelectedIndex);
        var route = UiInputRouteContext.HitTarget(new UiFocusController(), table.BuildInteractionFragment(frame).FocusEntries.Single().Target);
        (ScrollableListInputResult result, _) = table.RouteInput(new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false)), frame, route);
        Assert.Equal(ScrollableListInputResultKind.SelectionChanged, result.Kind);
        Assert.Equal(1, table.SelectedIndex);
    }

    [Fact]
    public void SectionBreak_MouseClickIsIgnored()
    {
        var table = new TableList<Item>([new("one", "", ""), new("two", "", "")], new TableListDefinition<Item>
        {
            Columns = [TableColumn<Item>.Text("Name", item => item.First, 8)],
            SectionBreakBetween = static (_, _) => true,
        });
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 8, 5));
        UiTargetId target = table.BuildInteractionFragment(frame).FocusEntries.Single().Target;

        (ScrollableListInputResult result, _) = table.RouteInput(new MouseConsoleInputEvent(0, 3, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None), frame, UiInputRouteContext.HitTarget(new UiFocusController(), target));

        Assert.Equal(ScrollableListInputResultKind.Handled, result.Kind);
        Assert.Equal(0, table.SelectedIndex);
    }

    private static TableList<Item> CreateTable(IReadOnlyList<Item> items, int selectedIndex = 0) => new(items, Definition(TableColumn<Item>.Text("Name", x => x.First, TableWidth.Flexible(8, 2)), TableColumn<Item>.Text("Size", x => x.Second, 4, TableColumnAlignment.Right)), selectedIndex);
    private static TableListDefinition<Item> Definition(params TableColumn<Item>[] columns) => new() { Columns = columns };
    private sealed record Item(string First, string Second, string Third);
}
