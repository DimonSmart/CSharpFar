using CSharpFar.App.Menu;
using CSharpFar.App.Panels;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationOverlayRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly Func<ConsolePalette> _palette;
    private readonly MenuLayoutService _menuLayoutService;
    private readonly MenuBarRenderer _menuBarRenderer = new();
    private readonly DropdownMenuRenderer _dropdownMenuRenderer = new();

    public ApplicationOverlayRenderer(
        ScreenRenderer screen,
        Func<ConsolePalette> palette,
        MenuLayoutService menuLayoutService)
    {
        _screen = screen;
        _palette = palette;
        _menuLayoutService = menuLayoutService;
    }

    public void RenderCommandCompletion(
        ConsoleSize size,
        int commandLineRow,
        CommandLine.CommandCompletionState completion)
    {
        if (!completion.Visible)
            return;

        new CommandHistoryCompletionRenderer(_screen, _palette()).Render(
            commandLineRow,
            size.Width,
            completion.Matches,
            completion.SelectedIndex,
            completion.FirstVisibleIndex);
    }

    public void RenderMenuOverlay(
        ConsoleSize size,
        MenuBarDefinition definition,
        MenuState menuState)
    {
        if (menuState.OpenState == MenuOpenState.Closed)
            return;

        var bounds = new Rect(0, 0, size.Width, size.Height);
        var layout = _menuLayoutService.CalculateLayout(bounds, definition, menuState);
        var options = BuildMenuRenderOptions(_palette());

        _menuBarRenderer.Render(_screen, bounds, definition, menuState, layout, options);
        _dropdownMenuRenderer.Render(_screen, definition, menuState, layout, options);
    }

    public bool RenderPanelQuickSearch(
        PanelQuickSearchState quickSearch,
        Rect leftBounds,
        Rect rightBounds,
        Func<PanelSide, bool> isPanelVisible)
    {
        if (!isPanelVisible(quickSearch.PanelSide))
            return false;

        var bounds = quickSearch.PanelSide == PanelSide.Left ? leftBounds : rightBounds;
        return new PanelQuickSearchRenderer(_screen, _palette())
            .Render(bounds, quickSearch.SearchText);
    }

    private static MenuRenderOptions BuildMenuRenderOptions(ConsolePalette palette) =>
        new()
        {
            MenuBarNormalStyle = new CellStyle(palette.MenuBarNormalFg, palette.MenuBarNormalBg),
            MenuBarActiveStyle = new CellStyle(palette.MenuBarActiveFg, palette.MenuBarActiveBg),
            NormalStyle = new CellStyle(palette.MenuNormalFg, palette.MenuNormalBg),
            ActiveStyle = new CellStyle(palette.MenuActiveFg, palette.MenuActiveBg),
            HighlightStyle = new CellStyle(palette.MenuHighlightFg, palette.MenuHighlightBg),
            ActiveHighlightStyle = new CellStyle(palette.MenuActiveHighlightFg, palette.MenuActiveHighlightBg),
            DisabledStyle = new CellStyle(palette.MenuDisabledFg, palette.MenuDisabledBg),
            BorderStyle = new CellStyle(palette.MenuBorderFg, palette.MenuBorderBg),
            ShadowStyle = new CellStyle(palette.MenuShadowFg, palette.MenuShadowBg),
        };
}
