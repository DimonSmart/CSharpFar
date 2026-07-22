using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class DirectoryShortcutBarRendererTests
{
    [Fact]
    public void Render_WithNoConfiguredShortcuts_WritesNothing()
    {
        var driver = new FakeConsoleDriver(80, 2);

        ApplicationDirectoryShortcutBarFrame? frame = UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            new DirectoryShortcutBarRenderer(canvas)
                .Render(0, 80, new AppSettings.DirectoryShortcutSettings()));

        Assert.Null(frame);
        Assert.Equal(new string(' ', 80), driver.GetRow(0));
    }

    [Fact]
    public void Render_WritesConfiguredSlotsInDisplayOrder()
    {
        var driver = new FakeConsoleDriver(80, 2);
        var settings = new AppSettings.DirectoryShortcutSettings
        {
            Items =
            [
                Item(0, "Temp", @"C:\Temp"),
                Item(3, "", @"C:\Three"),
                Item(1, "Projects", @"C:\Projects"),
            ],
        };

        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            new DirectoryShortcutBarRenderer(canvas).Render(0, 80, settings));

        Assert.StartsWith(" 1Projects 3 0Temp ", driver.GetRow(0));
    }

    [Fact]
    public void Render_FrameHitCarriesRenderedShortcutPath()
    {
        var driver = new FakeConsoleDriver(80, 2);
        var settings = new AppSettings.DirectoryShortcutSettings
        {
            Items = [Item(1, "Projects", @"C:\Projects")],
        };

        var frame = UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            new DirectoryShortcutBarRenderer(canvas).Render(0, 80, settings));

        var hit = Assert.Single(frame!.Shortcuts);
        Assert.Equal(1, hit.ShortcutNumber);
        Assert.Equal(@"C:\Projects", hit.Path);
    }

    [Fact]
    public void FrameSnapshotsShortcutList()
    {
        var hits = new List<ApplicationDirectoryShortcutHit>
        {
            new(new CSharpFar.Console.Models.Rect(1, 0, 9, 1), 1, @"C:\A"),
        };
        var frame = new ApplicationDirectoryShortcutBarFrame(hits);

        hits[0] = new ApplicationDirectoryShortcutHit(
            new CSharpFar.Console.Models.Rect(10, 0, 9, 1),
            2,
            @"C:\B");

        Assert.Equal(@"C:\A", Assert.Single(frame.Shortcuts).Path);
    }

    [Fact]
    public void Render_UsesDedicatedShortcutBarStylesOverPanelBorder()
    {
        var driver = new FakeConsoleDriver(80, 2);
        var settings = new AppSettings.DirectoryShortcutSettings
        {
            Items = [Item(1, "Projects", @"C:\Projects")],
        };

        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            new DirectoryShortcutBarRenderer(canvas).Render(0, 80, settings));

        var number = driver.GetCell(1, 0);
        Assert.Equal(ConsoleColor.White, number.Foreground);
        Assert.Equal(ConsoleColor.Black, number.Background);

        var label = driver.GetCell(2, 0);
        Assert.Equal(ConsoleColor.White, label.Foreground);
        Assert.Equal(ConsoleColor.Blue, label.Background);
        Assert.Equal("1Projects", driver.GetRow(0).Substring(1, 9));
    }

    private static AppSettings.DirectoryShortcutItem Item(int number, string name, string path) =>
        new()
        {
            Number = number,
            Name = name,
            Path = path,
        };
}
