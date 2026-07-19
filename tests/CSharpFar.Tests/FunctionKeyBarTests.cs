using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public class FunctionKeyBarTests
{
    private static readonly FunctionKeyBarItem[] PlainItems =
    [
        new(1,  "Help"),
        new(2,  "UserMn"),
        new(3,  "View"),
        new(4,  "Edit"),
        new(5,  "Copy"),
        new(6,  "RenMov"),
        new(7,  "MkFold"),
        new(8,  "Delete"),
        new(9,  "ConfMn"),
        new(10, "Quit"),
    ];

    [Theory]
    [InlineData(120, 10)]
    [InlineData(96, 8)]
    public void Render_UsesTwelveEqualSlots_WhenWidthAllows(int width, int slotWidth)
    {
        var driver = new FakeConsoleDriver(width, 2);
        var screen = new ScreenRenderer(driver);
        var renderer = CreateRenderer();

        Render(renderer, screen, y: 0, totalWidth: width, PlainItems);

        string row = driver.GetRow(0);
        for (int keyNumber = 1; keyNumber <= 12; keyNumber++)
        {
            int x = (keyNumber - 1) * slotWidth;
            string prefix = keyNumber.ToString();
            Assert.Equal(prefix, row.Substring(x, prefix.Length));
        }

        Assert.Contains("1Help", row);
        Assert.Contains("10Quit", row);
    }

    [Fact]
    public void Render_TruncatesLabelsWithEllipsis_WhenSlotIsTooSmall()
    {
        var driver = new FakeConsoleDriver(60, 2);
        var screen = new ScreenRenderer(driver);
        var renderer = CreateRenderer();
        FunctionKeyBarItem[] items = [new(1, "LongLabel")];

        Render(renderer, screen, y: 0, totalWidth: 60, items);

        Assert.StartsWith("1L...", driver.GetRow(0));
        Assert.Equal('2', driver.GetRow(0)[5]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_BlankLabelsForMissingActions(bool includeAvailableAction)
    {
        var driver = new FakeConsoleDriver(120, 2);
        var screen = new ScreenRenderer(driver);
        var renderer = CreateRenderer();
        FunctionKeyBarItem[] items = includeAvailableAction ? [new(7, "Search")] : [];

        Render(renderer, screen, y: 0, totalWidth: 120, items);

        string row = driver.GetRow(0);
        Assert.StartsWith("1         2", row);
        Assert.Contains("12        ", row);
        foreach (var item in items)
            Assert.Contains($"{item.KeyNumber}{item.Label}", row);
    }

    [Theory]
    [InlineData(57)]
    [InlineData(58)]
    [InlineData(59)]
    public void Render_KeepsStatusBarOnOneRow_ForOddAndEvenWidths(int width)
    {
        var driver = new FakeConsoleDriver(width, 2);
        var screen = new ScreenRenderer(driver);
        var renderer = CreateRenderer();

        Render(renderer, screen, y: 1, totalWidth: width, PlainItems);

        Assert.Equal(width, driver.GetRow(1).Length);
        Assert.Equal(new string(' ', width), driver.GetRow(0));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(122)]
    [InlineData(11)]
    public void BuildSlots_DefinesRenderedAndHitTestGeometry(int width)
    {
        var slots = FunctionKeyBar.BuildSlots(y: 3, totalWidth: width);

        if (width < 12)
        {
            Assert.Empty(slots);
            return;
        }

        Assert.Equal(12, slots.Count);
        Assert.Equal(new Rect(0, 3, width / 12, 1), slots[0].Bounds);
        Assert.Equal(width, slots[^1].Bounds.Right);
        Assert.Equal(width - (width / 12 * 11), slots[^1].Bounds.Width);

        foreach (var slot in slots)
        {
            Assert.True(FunctionKeyBar.TryGetKeyNumberAtX(slot.Bounds.X, width, out int keyNumber));
            Assert.Equal(slot.KeyNumber, keyNumber);
        }
    }

    [Fact]
    public void Render_SupportsAltFunctionKeysBeyondF10()
    {
        var driver = new FakeConsoleDriver(120, 2);
        var screen = new ScreenRenderer(driver);
        var renderer = CreateRenderer();
        FunctionKeyBarItem[] items =
        [
            new(1, "Left"),
            new(2, "Right"),
            new(7, "Search"),
            new(8, "History"),
            new(11, "FHist"),
            new(12, "DHist"),
        ];

        Render(renderer, screen, y: 0, totalWidth: 120, items);

        string row = driver.GetRow(0);
        Assert.Contains("1Left", row);
        Assert.Contains("7Search", row);
        Assert.Contains("11FHist", row);
        Assert.Contains("12DHist", row);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(8, 2)]
    [InlineData(72, 10)]
    [InlineData(96, 12)]
    [InlineData(99, 12)]
    public void HitTest_UsesRenderedSlotsIncludingLastRemainder(int x, int expectedKeyNumber)
    {
        Assert.True(FunctionKeyBar.TryGetKeyNumberAtX(x, totalWidth: 100, out int keyNumber));
        Assert.Equal(expectedKeyNumber, keyNumber);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void HitTest_RejectsPositionsOutsideBar(int x)
    {
        Assert.False(FunctionKeyBar.TryGetKeyNumberAtX(x, totalWidth: 100, out _));
    }

    [Theory]
    [InlineData(90, 24, MouseButton.Left, MouseEventKind.Down, true, 10)]
    [InlineData(90, 23, MouseButton.Left, MouseEventKind.Down, false, 0)]
    [InlineData(10, 24, MouseButton.Right, MouseEventKind.Down, false, 0)]
    [InlineData(10, 24, MouseButton.Middle, MouseEventKind.Down, false, 0)]
    [InlineData(10, 24, MouseButton.WheelDown, MouseEventKind.Wheel, false, 0)]
    [InlineData(10, 24, MouseButton.Left, MouseEventKind.Up, false, 0)]
    public void HitTest_RequiresBarRowAndActivationMouseEvent(
        int x,
        int y,
        MouseButton button,
        MouseEventKind kind,
        bool expectedResult,
        int expectedKeyNumber)
    {
        var mouse = new MouseConsoleInputEvent(x, y, button, kind, MouseKeyModifiers.None);

        Assert.Equal(expectedResult, FunctionKeyBar.TryGetKeyNumberAt(mouse, y: 24, totalWidth: 120, out int keyNumber));
        Assert.Equal(expectedKeyNumber, keyNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void HitTest_SmallWidthsDoNotThrow(int width)
    {
        var mouse = new MouseConsoleInputEvent(0, 0, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

        Assert.False(new FunctionKeyBar().TryHitTest(mouse, y: 0, totalWidth: width, out _));
    }

    [Fact]
    public void HitTest_MapsKeyNumberToFunctionKey()
    {
        Assert.True(FunctionKeyBar.TryGetFunctionKey(10, out var key));
        Assert.Equal(ConsoleKey.F10, key);
        Assert.False(FunctionKeyBar.TryGetFunctionKey(13, out _));
    }

    [Theory]
    [InlineData(MouseEventKind.Down)]
    public void Controller_ReturnsEnabledActionForActivationMouseEvents(MouseEventKind kind)
    {
        var controller = new FunctionKeyBarController<string>();
        FunctionKeyBarAction<string>[] actions =
        [
            new(1, "Help", "help"),
            new(10, "Quit", "quit"),
        ];
        var mouse = new MouseConsoleInputEvent(90, 24, MouseButton.Left, kind, MouseKeyModifiers.None);

        bool handled = controller.TryGetAction(mouse, y: 24, totalWidth: 120, actions, out string action);

        Assert.True(handled);
        Assert.Equal("quit", action);
    }

    [Fact]
    public void Controller_IgnoresDisabledAndMissingActions()
    {
        var controller = new FunctionKeyBarController<string>();
        FunctionKeyBarAction<string>[] actions =
        [
            new(10, "Quit", "quit", Enabled: false),
        ];
        var mouse = new MouseConsoleInputEvent(90, 24, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

        Assert.False(controller.TryGetAction(mouse, y: 24, totalWidth: 120, actions, out _));
    }

    [Fact]
    public void Controller_RenderFiltersDisabledActions()
    {
        var driver = new FakeConsoleDriver(120, 2);
        var screen = new ScreenRenderer(driver);
        var controller = new FunctionKeyBarController<string>();
        FunctionKeyBarAction<string>[] actions =
        [
            new(5, "Copy", "copy", Enabled: false),
            new(10, "Quit", "quit"),
        ];

        controller.Render(screen, y: 0, totalWidth: 120, actions);

        string row = driver.GetRow(0);
        Assert.DoesNotContain("5Copy", row);
        Assert.Contains("10Quit", row);
    }

    [Theory]
    [InlineData(0, 23, MouseButton.Left, MouseEventKind.Down)]
    [InlineData(0, 24, MouseButton.Right, MouseEventKind.Down)]
    [InlineData(0, 24, MouseButton.Left, MouseEventKind.Up)]
    public void Controller_IgnoresNonActivationMouseEvents(
        int x,
        int y,
        MouseButton button,
        MouseEventKind kind)
    {
        var controller = new FunctionKeyBarController<string>();
        FunctionKeyBarAction<string>[] actions = [new(1, "Help", "help")];
        var mouse = new MouseConsoleInputEvent(x, y, button, kind, MouseKeyModifiers.None);

        Assert.False(controller.TryGetAction(mouse, y: 24, totalWidth: 120, actions, out _));
    }

    private static FunctionKeyBar CreateRenderer() => new();

    private static void Render(
        FunctionKeyBar renderer,
        ScreenRenderer screen,
        int y,
        int totalWidth,
        IReadOnlyList<FunctionKeyBarItem> items) =>
        renderer.Render(screen, y, totalWidth, items);
}
