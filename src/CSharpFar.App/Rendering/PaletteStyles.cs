using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class PaletteStyles
{
    public static CellStyle PathHeaderActive(ConsolePalette palette) =>
        new(palette.PanelPathActiveFg, palette.PanelPathActiveBg);

    public static CellStyle DialogFill(ConsolePalette palette) =>
        new(palette.MenuNormalFg, palette.MenuNormalBg);

    public static CellStyle DialogBorder(ConsolePalette palette) =>
        new(palette.MenuBorderFg, palette.MenuBorderBg);

    public static CellStyle DialogTitle(ConsolePalette palette) =>
        new(palette.MenuNormalFg, palette.MenuNormalBg);

    public static CellStyle DialogHighlight(ConsolePalette palette) =>
        new(palette.MenuHighlightFg, palette.MenuHighlightBg);

    public static CellStyle DialogShadow(ConsolePalette palette) =>
        new(palette.MenuShadowFg, palette.MenuShadowBg);

    public static CellStyle InputField(ConsolePalette palette) =>
        new(palette.MenuActiveFg, palette.MenuActiveBg);

    public static CellStyle InputHighlight(ConsolePalette palette) =>
        new(palette.MenuActiveHighlightFg, palette.MenuActiveHighlightBg);

    public static CellStyle DialogError(ConsolePalette palette) =>
        new(palette.MenuHighlightFg, palette.MenuHighlightBg);

    public static PopupRenderOptions DialogPopupOptions(ConsolePalette palette) =>
        new()
        {
            BorderStyle = DialogBorder(palette),
            BackgroundStyle = DialogFill(palette),
            ShadowStyle = DialogShadow(palette),
            TitleStyle = DialogTitle(palette),
        };

    public static CellStyle CommandLine(ConsolePalette palette) =>
        new(palette.CommandLineFg, palette.CommandLineBg);

    public static CellStyle KeyBarNum(ConsolePalette palette) =>
        new(palette.FunctionKeyNumFg, palette.FunctionKeyNumBg);

    public static CellStyle KeyBarLabel(ConsolePalette palette) =>
        new(palette.FunctionKeyTextFg, palette.FunctionKeyBarBg);
}
