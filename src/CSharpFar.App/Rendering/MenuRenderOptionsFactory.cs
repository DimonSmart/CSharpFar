using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class MenuRenderOptionsFactory
{
    public static MenuRenderOptions Create(CSharpFarPalette palette) =>
        new()
        {
            MenuBarNormalStyle = new CellStyle(palette.Ui.MenuBarNormalFg, palette.Ui.MenuBarNormalBg),
            MenuBarActiveStyle = new CellStyle(palette.Ui.MenuBarActiveFg, palette.Ui.MenuBarActiveBg),
            NormalStyle = new CellStyle(palette.Ui.MenuNormalFg, palette.Ui.MenuNormalBg),
            ActiveStyle = new CellStyle(palette.Ui.MenuActiveFg, palette.Ui.MenuActiveBg),
            HighlightStyle = new CellStyle(palette.Ui.MenuHighlightFg, palette.Ui.MenuHighlightBg),
            ActiveHighlightStyle = new CellStyle(palette.Ui.MenuActiveHighlightFg, palette.Ui.MenuActiveHighlightBg),
            DisabledStyle = new CellStyle(palette.Ui.MenuDisabledFg, palette.Ui.MenuDisabledBg),
            BorderStyle = new CellStyle(palette.Ui.MenuBorderFg, palette.Ui.MenuBorderBg),
            ShadowStyle = new CellStyle(palette.Ui.MenuShadowFg, palette.Ui.MenuShadowBg),
        };
}
