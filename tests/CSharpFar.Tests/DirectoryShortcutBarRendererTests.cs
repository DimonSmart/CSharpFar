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

        new DirectoryShortcutBarRenderer(new ScreenRenderer(driver))
            .Render(0, 80, new AppSettings.DirectoryShortcutSettings());

        Assert.Empty(driver.WriteRecords);
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

        new DirectoryShortcutBarRenderer(new ScreenRenderer(driver)).Render(0, 80, settings);

        Assert.StartsWith(" 1Projects 3 0Temp ", driver.GetRow(0));
    }

    [Fact]
    public void Render_UsesDedicatedShortcutBarStylesOverPanelBorder()
    {
        var driver = new FakeConsoleDriver(80, 2);
        var settings = new AppSettings.DirectoryShortcutSettings
        {
            Items = [Item(1, "Projects", @"C:\Projects")],
        };

        new DirectoryShortcutBarRenderer(new ScreenRenderer(driver)).Render(0, 80, settings);

        var number = driver.GetCell(1, 0);
        Assert.Equal(ConsoleColor.Black, number.Foreground);
        Assert.Equal(ConsoleColor.Black, number.Background);

        var label = driver.GetCell(2, 0);
        Assert.Equal(ConsoleColor.White, label.Foreground);
        Assert.Equal(ConsoleColor.Blue, label.Background);
        Assert.Equal("1Projects", driver.GetRow(0).Substring(1, 9));
    }

    [Fact]
    public void TryGetShortcutNumberAt_ReturnsConfiguredSlotNumber()
    {
        var settings = new AppSettings.DirectoryShortcutSettings
        {
            Items =
            [
                Item(1, "Projects", @"C:\Projects"),
                Item(3, "", @"C:\Three"),
            ],
        };
        var mouse = new CSharpFar.Console.Input.MouseConsoleInputEvent(
            11,
            7,
            CSharpFar.Console.Input.MouseButton.Left,
            CSharpFar.Console.Input.MouseEventKind.Click,
            CSharpFar.Console.Input.MouseKeyModifiers.None);

        bool hit = DirectoryShortcutBarRenderer.TryGetShortcutNumberAt(
            mouse,
            barY: 7,
            totalWidth: 80,
            settings,
            out int number);

        Assert.True(hit);
        Assert.Equal(3, number);
    }

    private static AppSettings.DirectoryShortcutItem Item(int number, string name, string path) =>
        new()
        {
            Number = number,
            Name = name,
            Path = path,
        };
}
