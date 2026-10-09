using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class PanelSelectionPaletteTests
{
    [Fact]
    public void PanelSelectionColors_AreIndependentOfReusableUiSelectionColors()
    {
        var normal = CSharpFarPaletteRegistry.Default;
        Assert.Equal(ConsoleColor.Yellow, normal.PanelSelectedFg);
        Assert.Equal(ConsoleColor.DarkCyan, normal.PanelSelectedBg);
        Assert.Equal(ConsoleColor.Yellow, normal.Ui.SelectedFg);
        Assert.Equal(ConsoleColor.DarkBlue, normal.Ui.SelectedBg);

        var classic = CSharpFarPaletteRegistry.FarClassic;
        Assert.Equal(ConsoleColor.Black, classic.PanelSelectedFg);
        Assert.Equal(ConsoleColor.Green, classic.PanelSelectedBg);
        Assert.Equal(ConsoleColor.Black, classic.Ui.SelectedFg);
        Assert.Equal(ConsoleColor.Green, classic.Ui.SelectedBg);
    }

    [Theory]
    [InlineData(PanelViewMode.Full)]
    [InlineData(PanelViewMode.BriefTwoColumns)]
    public void SelectedRow_UsesPanelColorsInBothActiveAndInactivePanels(PanelViewMode mode)
    {
        foreach (var palette in CSharpFarPaletteRegistry.All)
        foreach (bool active in new[] { true, false })
        {
            var driver = new FakeConsoleDriver(40, 12);
            var screen = new ScreenRenderer(driver);
            FilePanelState state = MakeState();
            state.CursorIndex = 1;
            state.SelectedPaths.Add(state.Items[0].FullPath);

            UiTestRender.Render(screen, canvas =>
            {
                new PanelRenderer(canvas, palette).Render(
                    new Rect(0, 0, 40, 12), state, active, PanelSide.Left, mode);
            });

            int y = mode == PanelViewMode.Full ? 1 : 2;
            var cell = driver.GetCell(1, y);
            Assert.Equal(palette.PanelSelectedFg, cell.Foreground);
            Assert.Equal(palette.PanelSelectedBg, cell.Background);
        }
    }

    [Theory]
    [InlineData(PanelViewMode.Full)]
    [InlineData(PanelViewMode.BriefTwoColumns)]
    public void CursorTakesPrecedenceOverSelection(PanelViewMode mode)
    {
        var driver = new FakeConsoleDriver(40, 12);
        FilePanelState state = MakeState();
        state.CursorIndex = 0;
        state.SelectedPaths.Add(state.Items[0].FullPath);
        var palette = CSharpFarPaletteRegistry.Default;

        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
        {
            new PanelRenderer(canvas, palette).Render(
                new Rect(0, 0, 40, 12), state, true, PanelSide.Left, mode);
        });

        var cell = driver.GetCell(1, mode == PanelViewMode.Full ? 1 : 2);
        Assert.Equal(palette.CursorActiveFg, cell.Foreground);
        Assert.Equal(palette.CursorActiveBg, cell.Background);
    }

    private static FilePanelState MakeState()
    {
        var state = new FilePanelState { CurrentDirectory = @"C:\work" };
        foreach (string name in new[] { "a.txt", "b.txt" })
        {
            state.Items.Add(new FilePanelItem
            {
                Name = name,
                FullPath = $@"C:\work\{name}",
                IsDirectory = false,
            });
        }
        return state;
    }
}
