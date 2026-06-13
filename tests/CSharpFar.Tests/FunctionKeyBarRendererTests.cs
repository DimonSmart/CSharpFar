using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.App.Rendering;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public class FunctionKeyBarRendererTests
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
        var renderer = CreateRenderer(new ScreenRenderer(driver));

        Render(renderer, y: 0, totalWidth: width, PlainItems);

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
        var renderer = CreateRenderer(new ScreenRenderer(driver));
        FunctionKeyBarItem[] items = [new(1, "LongLabel")];

        Render(renderer, y: 0, totalWidth: 60, items);

        Assert.StartsWith("1L...", driver.GetRow(0));
        Assert.Equal('2', driver.GetRow(0)[5]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_BlankLabelsForMissingActions(bool includeAvailableAction)
    {
        var driver = new FakeConsoleDriver(120, 2);
        var renderer = CreateRenderer(new ScreenRenderer(driver));
        FunctionKeyBarItem[] items = includeAvailableAction ? [new(7, "Search")] : [];

        Render(renderer, y: 0, totalWidth: 120, items);

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
        var renderer = CreateRenderer(new ScreenRenderer(driver));

        Render(renderer, y: 1, totalWidth: width, PlainItems);

        Assert.Equal(width, driver.GetRow(1).Length);
        Assert.Equal(new string(' ', width), driver.GetRow(0));
    }

    [Fact]
    public void Render_SupportsAltFunctionKeysBeyondF10()
    {
        var driver = new FakeConsoleDriver(120, 2);
        var renderer = CreateRenderer(new ScreenRenderer(driver));
        FunctionKeyBarItem[] items =
        [
            new(1, "Left"),
            new(2, "Right"),
            new(7, "Search"),
            new(8, "History"),
            new(11, "FHist"),
            new(12, "DHist"),
        ];

        Render(renderer, y: 0, totalWidth: 120, items);

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
        Assert.True(FunctionKeyBarRenderer.TryGetKeyNumberAtX(x, totalWidth: 100, out int keyNumber));
        Assert.Equal(expectedKeyNumber, keyNumber);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void HitTest_RejectsPositionsOutsideBar(int x)
    {
        Assert.False(FunctionKeyBarRenderer.TryGetKeyNumberAtX(x, totalWidth: 100, out _));
    }

    [Fact]
    public void HitTest_MouseRequiresBarRowAndActivationClick()
    {
        var mouse = new MouseConsoleInputEvent(90, 24, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

        Assert.True(FunctionKeyBarRenderer.TryGetKeyNumberAt(mouse, barY: 24, totalWidth: 120, out int keyNumber));
        Assert.Equal(10, keyNumber);
        Assert.False(FunctionKeyBarRenderer.TryGetKeyNumberAt(mouse, barY: 23, totalWidth: 120, out _));
    }

    [Fact]
    public void HitTest_MapsKeyNumberToFunctionKey()
    {
        Assert.True(FunctionKeyBarRenderer.TryGetFunctionKey(10, out var key));
        Assert.Equal(ConsoleKey.F10, key);
        Assert.False(FunctionKeyBarRenderer.TryGetFunctionKey(13, out _));
    }

    private static FunctionKeyBarRenderer CreateRenderer(ScreenRenderer screen) => new(screen);

    private static void Render(
        FunctionKeyBarRenderer renderer,
        int y,
        int totalWidth,
        IReadOnlyList<FunctionKeyBarItem> items) =>
        renderer.Render(y, totalWidth, items);
}
