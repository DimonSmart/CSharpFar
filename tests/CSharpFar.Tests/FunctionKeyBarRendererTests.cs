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

    [Fact]
    public void Render_UsesTwelveFixedSlots_WhenWidthAllows()
    {
        var driver = new FakeConsoleDriver(120, 2);
        var renderer = CreateRenderer(new ScreenRenderer(driver));

        Render(renderer, y: 0, totalWidth: 120, PlainItems);

        string row = driver.GetRow(0);
        Assert.Equal('1', row[0]);
        Assert.Equal('2', row[10]);
        Assert.Equal('3', row[20]);
        Assert.Equal('9', row[80]);
        Assert.Equal('1', row[90]);
        Assert.Equal('0', row[91]);
        Assert.Equal('1', row[100]);
        Assert.Equal('1', row[101]);
        Assert.Equal('1', row[110]);
        Assert.Equal('2', row[111]);
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

    [Fact]
    public void Render_BlankLabelsForMissingActions()
    {
        var driver = new FakeConsoleDriver(120, 2);
        var renderer = CreateRenderer(new ScreenRenderer(driver));
        FunctionKeyBarItem[] items = [new(7, "Search")];

        Render(renderer, y: 0, totalWidth: 120, items);

        string row = driver.GetRow(0);
        Assert.StartsWith("1         2", row);
        Assert.Contains("7Search", row);
        Assert.Contains("12        ", row);
    }

    [Fact]
    public void Render_UsesFarLikeFunctionKeyColors()
    {
        var driver = new FakeConsoleDriver(120, 2);
        var renderer = CreateRenderer(new ScreenRenderer(driver));

        Render(renderer, y: 0, totalWidth: 120, PlainItems);

        var number = driver.GetCell(0, 0);
        Assert.Equal('1', number.Character);
        Assert.Equal(ConsoleColor.White, number.Foreground);
        Assert.Equal(ConsoleColor.Black, number.Background);

        for (int x = 1; x <= 5; x++)
        {
            var cell = driver.GetCell(x, 0);
            Assert.Equal(ConsoleColor.Black, cell.Foreground);
            Assert.Equal(ConsoleColor.DarkCyan, cell.Background);
        }
    }

    [Fact]
    public void Render_KeepsAllButtonWidthsEqual()
    {
        var driver = new FakeConsoleDriver(96, 2);
        var renderer = CreateRenderer(new ScreenRenderer(driver));

        Render(renderer, y: 0, totalWidth: 96, PlainItems);

        string row = driver.GetRow(0);
        Assert.Equal('1', row[0]);
        Assert.Equal('2', row[8]);
        Assert.Equal('3', row[16]);
        Assert.Equal('1', row[72]);
        Assert.Equal('0', row[73]);
        Assert.Equal('1', row[80]);
        Assert.Equal('1', row[81]);
        Assert.Equal('1', row[88]);
        Assert.Equal('2', row[89]);
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

    [Fact]
    public void Render_ShowsOnlyNumbers_WhenNoItemsAreAvailable()
    {
        var driver = new FakeConsoleDriver(120, 2);
        var renderer = CreateRenderer(new ScreenRenderer(driver));

        Render(renderer, y: 0, totalWidth: 120, []);

        string row = driver.GetRow(0);
        Assert.StartsWith("1         2", row);
        Assert.Contains("10        ", row);
        Assert.Contains("11        ", row);
        Assert.Contains("12        ", row);
        Assert.DoesNotContain("Help", row);
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
