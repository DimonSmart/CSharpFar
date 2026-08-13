using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TableListTests
{
    [Fact]
    public void Render_UsesColumnsSeparatorsAlignmentUnicodeAndSelectionWithoutTrailingSeparator()
    {
        var table = CreateTable([new("表long", "42")], selectedIndex: 0);
        var driver = new FakeConsoleDriver(20, 4);
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 20, 3));

        UiTestRender.Render(new ScreenRenderer(driver), canvas => table.Render(canvas, frame, CreatePresentation()));

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
        var table = CreateTable([new("Alpha", "7")]);
        var narrow = new FakeConsoleDriver(6, 3);
        var wide = new FakeConsoleDriver(24, 3);

        UiTestRender.Render(new ScreenRenderer(narrow), canvas => table.Render(canvas, table.CalculateFrame(new Rect(0, 0, 6, 3)), CreatePresentation()));
        UiTestRender.Render(new ScreenRenderer(wide), canvas => table.Render(canvas, table.CalculateFrame(new Rect(0, 0, 24, 3)), CreatePresentation()));

        Assert.Equal("Name │", narrow.GetRow(0));
        Assert.Equal("Name │ Size             ", wide.GetRow(0));
        Assert.DoesNotContain('│', wide.GetRow(0).Skip(11));
        Assert.DoesNotContain('│', wide.GetRow(2).Skip(11));
    }

    [Fact]
    public void State_ExposesInitialAndSelectedItemAndHandlesKeyboardNavigation()
    {
        var table = CreateTable([new("One", "1"), new("Two", "2")], selectedIndex: 1);
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 20, 4));
        UiInteractionFrame interaction = table.BuildInteractionFrame(frame);

        Assert.True(table.HasItems);
        Assert.Equal(2, table.Count);
        Assert.Equal(1, table.SelectedIndex);
        Assert.Equal("Two", table.SelectedItem!.Name);

        var route = UiInputRouteContext.KeyboardTarget(new UiFocusController(), interaction.KeyboardTarget!);
        (ScrollableListInputResult result, _) = table.RouteInput(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false)), frame, route);

        Assert.Equal(ScrollableListInputResultKind.SelectionChanged, result.Kind);
        Assert.Equal(0, table.SelectedIndex);
        Assert.Equal("One", table.SelectedItem!.Name);
    }

    [Fact]
    public void CalculateFrame_UsesSemanticBodyAndCreatesScrollbarForOverflow()
    {
        var table = CreateTable(Enumerable.Range(0, 5).Select(index => new Item($"Item {index}", index.ToString())).ToArray());

        TableListFrame frame = table.CalculateFrame(new Rect(2, 3, 20, 5));

        Assert.Equal(new Rect(2, 3, 20, 2), frame.HeaderBounds);
        Assert.Equal(new Rect(2, 5, 19, 3), frame.BodyBounds);
        Assert.Equal(3, frame.ViewportRows);
        Assert.True(frame.HasScrollbar);
    }

    [Fact]
    public void Scrollbar_IsEntirelyInsideTableBounds()
    {
        var table = CreateTable(Enumerable.Range(0, 5).Select(index => new Item($"Item {index}", index.ToString())).ToArray());
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 20, 5));
        UiInteractionFragment fragment = table.BuildInteractionFragment(frame);
        UiHitRegion scrollbar = Assert.Single(fragment.HitRegions, region => region.Bounds.X == 19);
        var driver = new FakeConsoleDriver(21, 5);

        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
        {
            canvas.Write(20, 2, "X", CreatePresentation().Normal);
            table.Render(canvas, frame, CreatePresentation());
        });

        Assert.Equal(new Rect(19, 2, 1, 3), scrollbar.Bounds);
        Assert.True(frame.Bounds.Contains(scrollbar.Bounds.X, scrollbar.Bounds.Y));
        Assert.True(frame.Bounds.Contains(scrollbar.Bounds.Right - 1, scrollbar.Bounds.Bottom - 1));
    }

    [Fact]
    public void Scrollbar_DoesNotOverwriteAdjacentCell()
    {
        var table = CreateTable(Enumerable.Range(0, 5).Select(index => new Item($"Item {index}", index.ToString())).ToArray());
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 20, 5));
        var driver = new FakeConsoleDriver(21, 5);

        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
        {
            canvas.Write(20, 2, "X", CreatePresentation().Normal);
            table.Render(canvas, frame, CreatePresentation());
        });

        Assert.Equal('X', driver.GetCell(20, 2).Character);
    }

    [Fact]
    public void Overflow_ReservesOneColumnForScrollbar()
    {
        var overflow = CreateTable(Enumerable.Range(0, 5).Select(index => new Item($"Item {index}", index.ToString())).ToArray());

        TableListFrame overflowFrame = overflow.CalculateFrame(new Rect(4, 6, 20, 5));
        UiInteractionFragment overflowFragment = overflow.BuildInteractionFragment(overflowFrame);

        Assert.Equal(new Rect(4, 8, 19, 3), overflowFrame.BodyBounds);
        Assert.Contains(overflowFragment.HitRegions, region => region.Bounds.Equals(new Rect(23, 8, 1, 3)));
    }

    [Fact]
    public void NoOverflow_UsesFullBodyWidth()
    {
        var noOverflow = CreateTable([new Item("Item", "1")]);

        TableListFrame noOverflowFrame = noOverflow.CalculateFrame(new Rect(4, 6, 20, 5));
        UiInteractionFragment noOverflowFragment = noOverflow.BuildInteractionFragment(noOverflowFrame);

        Assert.Equal(new Rect(4, 8, 20, 3), noOverflowFrame.BodyBounds);
        Assert.DoesNotContain(noOverflowFragment.HitRegions, region => region.Bounds.X == 23);
    }

    [Fact]
    public void ScrollbarMouseInput_IsHandled()
    {
        var table = CreateTable(Enumerable.Range(0, 5).Select(index => new Item($"Item {index}", index.ToString())).ToArray());
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 20, 5));
        UiInteractionFragment fragment = table.BuildInteractionFragment(frame);
        UiHitRegion scrollbar = Assert.Single(fragment.HitRegions, region => region.Bounds.X == 19);
        var focus = new UiFocusController();

        (ScrollableListInputResult scrollbarResult, UiInputResult scrollbarUi) = table.RouteInput(
            new MouseConsoleInputEvent(scrollbar.Bounds.X, scrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            UiInputRouteContext.HitTarget(focus, scrollbar.Target));
        Assert.True(scrollbarResult.IsHandled);
        Assert.True(scrollbarUi.Handled);
    }

    [Fact]
    public void UnrelatedKey_RemainsNotHandled()
    {
        var table = CreateTable(Enumerable.Range(0, 5).Select(index => new Item($"Item {index}", index.ToString())).ToArray());
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 20, 5));
        var focus = new UiFocusController();

        (ScrollableListInputResult keyResult, UiInputResult keyUi) = table.RouteInput(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false)),
            frame,
            UiInputRouteContext.KeyboardTarget(focus, table.BuildInteractionFrame(frame).KeyboardTarget!));

        Assert.Equal(ScrollableListInputResultKind.NotHandled, keyResult.Kind);
        Assert.False(keyUi.Handled);
    }

    [Fact]
    public void State_EmptyItemsHaveNoSelectionOrInteractionTarget()
    {
        var table = CreateTable([]);
        TableListFrame frame = table.CalculateFrame(new Rect(0, 0, 20, 3));

        Assert.False(table.HasItems);
        Assert.Equal(-1, table.SelectedIndex);
        Assert.Null(table.SelectedItem);
        Assert.Empty(table.BuildInteractionFrame(frame).Focus.Entries);
    }

    private static TableListPresentation CreatePresentation()
    {
        var normal = new CellStyle(ConsoleColor.White, ConsoleColor.Black);
        return new()
        {
            Header = normal,
            Separator = normal,
            Normal = normal,
            Selected = new CellStyle(ConsoleColor.Black, ConsoleColor.White),
            Emphasized = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black),
            EmphasizedSelected = new CellStyle(ConsoleColor.Yellow, ConsoleColor.White),
            Scrollbar = normal,
        };
    }

    private static TableList<Item> CreateTable(IReadOnlyList<Item> items, int selectedIndex = 0) => new(
        items,
        new TableListDefinition<Item>
        {
            Columns =
            [
                TableColumn<Item>.Text("Name", item => item.Name, 4),
                TableColumn<Item>.Text("Size", item => item.Size, 4, TableColumnAlignment.Right, emphasized: true),
            ],
        },
        selectedIndex);

    private sealed record Item(string Name, string Size);
}
