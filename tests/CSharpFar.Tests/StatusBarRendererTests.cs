using System.Reflection;
using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public class StatusBarRendererTests
{
    [Fact]
    public void Render_FitsFullMenu_WhenWidthAllows()
    {
        var driver = new FakeConsoleDriver(80, 2);
        var renderer = CreateRenderer(new ScreenRenderer(driver));

        Render(renderer, y: 0, totalWidth: 80);

        Assert.StartsWith("1Help2Menu3View4Edit", driver.GetRow(0));
        Assert.Contains("10Quit", driver.GetRow(0));
    }

    [Fact]
    public void Render_TruncatesWithRedEllipsis_WhenMenuDoesNotFit()
    {
        var driver = new FakeConsoleDriver(5, 2);
        var renderer = CreateRenderer(new ScreenRenderer(driver));

        Render(renderer, y: 0, totalWidth: 5);

        Assert.Equal("1H...", driver.GetRow(0));
        for (int x = 2; x < 5; x++)
        {
            var cell = driver.GetCell(x, 0);
            Assert.Equal('.', cell.Character);
            Assert.Equal(ConsoleColor.Red, cell.Foreground);
            Assert.Equal(ConsoleColor.DarkGray, cell.Background);
        }
    }

    [Theory]
    [InlineData(57)]
    [InlineData(58)]
    [InlineData(59)]
    public void Render_KeepsStatusBarOnOneRow_ForOddAndEvenWidths(int width)
    {
        var driver = new FakeConsoleDriver(width, 2);
        var renderer = CreateRenderer(new ScreenRenderer(driver));

        Render(renderer, y: 1, totalWidth: width);

        Assert.Equal(width, driver.GetRow(1).Length);
        Assert.Equal(new string(' ', width), driver.GetRow(0));
    }

    private static object CreateRenderer(ScreenRenderer screen)
    {
        var type = typeof(Application).Assembly.GetType("CSharpFar.App.Rendering.StatusBarRenderer")
            ?? throw new InvalidOperationException("StatusBarRenderer type not found.");

        return Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [screen],
            culture: null) ?? throw new InvalidOperationException("Could not create StatusBarRenderer.");
    }

    private static void Render(object renderer, int y, int totalWidth)
    {
        var method = renderer.GetType().GetMethod(
            "Render",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("StatusBarRenderer.Render method not found.");

        method.Invoke(renderer, [y, totalWidth]);
    }
}
