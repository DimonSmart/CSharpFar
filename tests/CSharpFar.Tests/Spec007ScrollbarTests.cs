using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Stage 7 – Scrollbar: ScrollBarRenderer rendering behavior.
/// </summary>
public sealed class Spec007ScrollbarTests
{
    // ── Disabled ──────────────────────────────────────────────────────────────

    [Fact]
    public void Disabled_NoCharsWritten()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 10);
        var screen = new ScreenRenderer(driver);
        var renderer = new ScrollBarRenderer();
        var opts = new ScrollBarOptions { Enabled = false };
        var state = new ScrollState { TotalItems = 20, ViewportItems = 5, FirstVisibleIndex = 0 };

        using (screen.BeginFrame())
            renderer.RenderVerticalScrollbar(screen, new Rect(0, 0, 1, 7), state, opts,
                new CellStyle(ConsoleColor.White, ConsoleColor.Black));

        // Nothing drawn; cells stay at initial ' ' (space)
        Assert.All(Enumerable.Range(0, 7),
            y => Assert.Equal(' ', driver.GetCell(0, y).Character));
    }

    // ── Enabled, scrollable ───────────────────────────────────────────────────

    [Fact]
    public void Enabled_Scrollable_DrawsArrows()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 10);
        var screen = new ScreenRenderer(driver);
        var renderer = new ScrollBarRenderer();
        var bounds = new Rect(0, 0, 1, 7);
        var state = new ScrollState { TotalItems = 20, ViewportItems = 5, FirstVisibleIndex = 0 };
        var opts = new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false };
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkGray);

        using (screen.BeginFrame())
            renderer.RenderVerticalScrollbar(screen, bounds, state, opts, style);

        Assert.Equal('▲', driver.GetCell(0, 0).Character);
        Assert.Equal('▼', driver.GetCell(0, 6).Character);
    }

    [Fact]
    public void Enabled_Scrollable_DrawsThumb()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 10);
        var screen = new ScreenRenderer(driver);
        var renderer = new ScrollBarRenderer();
        var bounds = new Rect(0, 0, 1, 7);
        var state = new ScrollState { TotalItems = 20, ViewportItems = 5, FirstVisibleIndex = 0 };
        var opts = new ScrollBarOptions { Enabled = true };
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkGray);

        using (screen.BeginFrame())
            renderer.RenderVerticalScrollbar(screen, bounds, state, opts, style);

        // Track is rows 1-5; at least one row should be '█' (thumb)
        bool hasThumb = Enumerable.Range(1, 5).Any(y => driver.GetCell(0, y).Character == '█');
        Assert.True(hasThumb, "Thumb not drawn in scrollable mode");
    }

    [Fact]
    public void Enabled_NotScrollable_DrawWhenNotScrollable_False_NothingDrawn()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 10);
        var screen = new ScreenRenderer(driver);
        var renderer = new ScrollBarRenderer();
        var bounds = new Rect(0, 0, 1, 7);
        // Total == Viewport → not scrollable
        var state = new ScrollState { TotalItems = 5, ViewportItems = 5, FirstVisibleIndex = 0 };
        var opts = new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false };

        using (screen.BeginFrame())
            renderer.RenderVerticalScrollbar(screen, bounds, state, opts,
                new CellStyle(ConsoleColor.White, ConsoleColor.Black));

        Assert.All(Enumerable.Range(0, 7),
            y => Assert.Equal(' ', driver.GetCell(0, y).Character));
    }

    [Fact]
    public void ThumbPosition_ChangesWithFirstVisibleIndex()
    {
        var driver1 = new FakeConsoleDriver(width: 20, height: 15);
        var screen1 = new ScreenRenderer(driver1);

        var driver2 = new FakeConsoleDriver(width: 20, height: 15);
        var screen2 = new ScreenRenderer(driver2);

        var renderer = new ScrollBarRenderer();
        var bounds = new Rect(0, 0, 1, 12);
        var opts = new ScrollBarOptions { Enabled = true };
        var style = new CellStyle(ConsoleColor.White, ConsoleColor.DarkGray);

        using (screen1.BeginFrame())
            renderer.RenderVerticalScrollbar(screen1, bounds,
                new ScrollState { TotalItems = 40, ViewportItems = 10, FirstVisibleIndex = 0 },
                opts, style);

        using (screen2.BeginFrame())
            renderer.RenderVerticalScrollbar(screen2, bounds,
                new ScrollState { TotalItems = 40, ViewportItems = 10, FirstVisibleIndex = 30 },
                opts, style);

        // Find first thumb row in each (track rows 1-10)
        int thumb1 = Enumerable.Range(1, 10).First(y => driver1.GetCell(0, y).Character == '█');
        int thumb2 = Enumerable.Range(1, 10).First(y => driver2.GetCell(0, y).Character == '█');

        Assert.True(thumb2 > thumb1,
            $"Thumb at index 30 ({thumb2}) should be lower than at index 0 ({thumb1})");
    }

    [Fact]
    public void FullPanel_Scrollable_RendersScrollbarOnRightBorder()
    {
        var driver = new FakeConsoleDriver(width: 40, height: 12);
        var screen = new ScreenRenderer(driver);
        var state = new FilePanelState { CurrentDirectory = @"C:\Root" };
        for (int i = 0; i < 30; i++)
        {
            state.Items.Add(new FilePanelItem
            {
                Name = $"file{i:D2}.txt",
                FullPath = $@"C:\Root\file{i:D2}.txt",
                IsDirectory = false,
            });
        }

        var renderer = new PanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, 12), state, isActive: true);

        Assert.Equal('▲', driver.GetCell(39, 1).Character);
    }

    [Fact]
    public void BriefPanel_Scrollable_RendersScrollbarOnRightBorder()
    {
        var driver = new FakeConsoleDriver(width: 40, height: 12);
        var screen = new ScreenRenderer(driver);
        var state = new FilePanelState { CurrentDirectory = @"C:\Root" };
        for (int i = 0; i < 30; i++)
        {
            state.Items.Add(new FilePanelItem
            {
                Name = $"file{i:D2}.txt",
                FullPath = $@"C:\Root\file{i:D2}.txt",
                IsDirectory = false,
            });
        }

        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, 12), state, isActive: true);

        Assert.Equal('▲', driver.GetCell(39, 2).Character);
    }

    [Fact]
    public void FullPanel_NotScrollable_DoesNotRenderScrollbar()
    {
        var driver = new FakeConsoleDriver(width: 40, height: 12);
        var screen = new ScreenRenderer(driver);
        var state = new FilePanelState { CurrentDirectory = @"C:\Root" };
        state.Items.Add(new FilePanelItem
        {
            Name = "file.txt",
            FullPath = @"C:\Root\file.txt",
            IsDirectory = false,
        });

        var renderer = new PanelRenderer(screen, PaletteRegistry.Default);

        renderer.Render(new Rect(0, 0, 40, 12), state, isActive: true);

        Assert.Equal('║', driver.GetCell(39, 1).Character);
    }
}
