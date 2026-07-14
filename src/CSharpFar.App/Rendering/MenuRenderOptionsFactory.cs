using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class MenuRenderOptionsFactory
{
    public static MenuRenderOptions Create(ConsolePalette palette) =>
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
