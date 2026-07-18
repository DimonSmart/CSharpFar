using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies that file highlighting is applied only to the file name cell
/// (not to size column, separator, or other chrome) in both Full and BriefTwoColumns modes.
/// </summary>
public class HighlightRendererTests
{
    private static (ScreenRenderer screen, FakeConsoleDriver driver) MakeScreen(int w = 60, int h = 15)
    {
        var drv = new FakeConsoleDriver(w, h);
        return (new ScreenRenderer(drv), drv);
    }

    private static FileHighlightService FarDefaultService()
        => new(FarDefaultHighlightPreset.Rules,
               FarDefaultHighlightPreset.GroupsByName,
               pathExt: "");

    private static FilePanelState MakeState(params FilePanelItem[] items)
    {
        var state = new FilePanelState { CurrentDirectory = @"C:\Test" };
        foreach (var i in items) state.Items.Add(i);
        return state;
    }

    private static FilePanelItem MakeFile(string name, FileAttributes attrs = default) =>
        new() { Name = name, FullPath = @"C:\Test\" + name, IsDirectory = false, Attributes = attrs };

    private static FilePanelItem MakeDir(string name) =>
        new()
        {
            Name = name,
            FullPath = @"C:\Test\" + name,
            IsDirectory = true,
            Attributes = FileAttributes.Directory
        };

    // ── Full mode ─────────────────────────────────────────────────────────────

    [Fact]
    public void FullMode_Colors_Only_FileName_Not_SizeColumn()
    {
        var (screen, driver) = MakeScreen(60, 10);
        var svc = FarDefaultService();
        var state = MakeState(MakeFile("app.exe"));
        state.CursorIndex = -1; // no cursor row → item uses fileStyle as base
        var bounds = new Rect(0, 0, 60, 10);

        var renderer = new PanelRenderer(screen, PaletteRegistry.Default, svc);
        renderer.Render(bounds, state, isActive: true);

        // Compute column geometry
        int innerWidth = bounds.Width - 2; // 58
        int sizeCol = Math.Min(8, innerWidth / 3); // 8
        int nameCol = innerWidth - sizeCol - 1;    // 49

        int fileRow = bounds.Y + 1; // first content row

        // Name cell (x=1..nameCol) must use highlight foreground (Green = 10)
        var nameCell = driver.GetCell(1, fileRow);
        Assert.Equal(ConsoleColor.Green, nameCell.Foreground);

        // Size cell (x = 1 + nameCol + 1) must use normal foreground (White = 15)
        var sizeCell = driver.GetCell(1 + nameCol + 1, fileRow);
        Assert.Equal(PaletteRegistry.Default.NormalFileFg, sizeCell.Foreground);
        Assert.NotEqual(ConsoleColor.Green, sizeCell.Foreground);
    }

    [Fact]
    public void FullMode_Disabled_Highlighting_Returns_Palette_Colors()
    {
        var (screen, driver) = MakeScreen(60, 10);
        // No highlight service
        var state = MakeState(MakeFile("app.exe"));
        state.CursorIndex = -1; // no cursor row → item uses fileStyle (NormalFileFg)
        var bounds = new Rect(0, 0, 60, 10);

        var renderer = new PanelRenderer(screen, PaletteRegistry.Default);
        renderer.Render(bounds, state, isActive: true);

        // Without highlight, file name foreground is NormalFileFg
        var nameCell = driver.GetCell(1, bounds.Y + 1);
        Assert.Equal(PaletteRegistry.Default.NormalFileFg, nameCell.Foreground);
    }

    [Fact]
    public void FullMode_Directory_Highlighted_As_White()
    {
        var (screen, driver) = MakeScreen(60, 10);
        var svc = FarDefaultService();
        var state = MakeState(MakeDir("src"));
        var bounds = new Rect(0, 0, 60, 10);

        var renderer = new PanelRenderer(screen, PaletteRegistry.Default, svc);
        renderer.Render(bounds, state, isActive: true);

        // far.directory Normal = fg 15 = White
        var nameCell = driver.GetCell(1, bounds.Y + 1);
        Assert.Equal(ConsoleColor.White, nameCell.Foreground);
    }

    // ── BriefTwoColumns mode ──────────────────────────────────────────────────

    [Fact]
    public void BriefMode_Colors_Only_ItemCell_Not_Separator()
    {
        var (screen, driver) = MakeScreen(40, 12);
        var svc = FarDefaultService();
        var state = MakeState(MakeFile("app.exe"));
        var bounds = new Rect(0, 0, 40, 12);

        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default, svc);
        renderer.Render(bounds, state, isActive: true);

        int innerWidth = bounds.Width - 2; // 38
        int sepOffset = innerWidth / 2;   // 19
        int col1X = 1;
        int sepX = 1 + sepOffset;    // 20
        int contentRow = bounds.Y + 2;

        // Name cell: Green (fg 10)
        var nameCell = driver.GetCell(col1X, contentRow);
        Assert.Equal(ConsoleColor.Green, nameCell.Foreground);

        // Separator: border color (White on DarkBlue for Default), NOT Green
        var sepCell = driver.GetCell(sepX, contentRow);
        Assert.NotEqual(ConsoleColor.Green, sepCell.Foreground);
    }

    [Fact]
    public void BriefMode_Disabled_Highlighting_Returns_Palette_Colors()
    {
        var (screen, driver) = MakeScreen(40, 12);
        var state = MakeState(MakeFile("app.exe"));
        state.CursorIndex = -1; // no cursor row → item uses fileStyle (NormalFileFg)
        var bounds = new Rect(0, 0, 40, 12);

        // No highlight service
        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default);
        renderer.Render(bounds, state, isActive: true);

        var nameCell = driver.GetCell(1, bounds.Y + 2);
        Assert.Equal(PaletteRegistry.Default.NormalFileFg, nameCell.Foreground);
    }

    // ── Inactive panel – base background inherited ────────────────────────────

    [Fact]
    public void FullMode_InactivePanel_HighlightInherits_PanelBackground()
    {
        var (screen, driver) = MakeScreen(60, 10);
        var svc = FarDefaultService();
        var state = MakeState(MakeFile("app.exe"));
        var bounds = new Rect(0, 0, 60, 10);

        var renderer = new PanelRenderer(screen, PaletteRegistry.Default, svc);
        renderer.Render(bounds, state, isActive: false);

        // Inactive panel, normal row: highlight fg=Green, bg inherited from fileStyle=PanelBackground
        var nameCell = driver.GetCell(1, bounds.Y + 1);
        Assert.Equal(ConsoleColor.Green, nameCell.Foreground);
        Assert.Equal(PaletteRegistry.Default.PanelBackground, nameCell.Background);
    }

    [Fact]
    public void BriefMode_InactivePanel_HighlightInherits_PanelBackground()
    {
        var (screen, driver) = MakeScreen(40, 12);
        var svc = FarDefaultService();
        var state = MakeState(MakeFile("app.exe"));
        var bounds = new Rect(0, 0, 40, 12);
        state.CursorIndex = 0;

        var renderer = new BriefTwoColumnsPanelRenderer(screen, PaletteRegistry.Default, svc);
        renderer.Render(bounds, state, isActive: false);

        // Inactive panel cursor item: isCursor=false, uses fileStyle base
        // Highlight fg=Green, bg inherited from PanelBackground
        var nameCell = driver.GetCell(1, bounds.Y + 2);
        Assert.Equal(ConsoleColor.Green, nameCell.Foreground);
        Assert.Equal(PaletteRegistry.Default.PanelBackground, nameCell.Background);
    }
}
