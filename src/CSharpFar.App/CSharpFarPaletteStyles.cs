using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App;

public static class CSharpFarPaletteStyles
{
    public static CellStyle DialogFill(CSharpFarPalette p) => PaletteStyles.DialogFill(p.Ui);
    public static CellStyle DialogBorder(CSharpFarPalette p) => PaletteStyles.DialogBorder(p.Ui);
    public static CellStyle InputField(CSharpFarPalette p) => PaletteStyles.InputField(p.Ui);
    public static CellStyle InputHighlight(CSharpFarPalette p) => PaletteStyles.InputHighlight(p.Ui);
    public static PopupRenderOptions DialogPopupOptions(CSharpFarPalette p) => PaletteStyles.DialogPopupOptions(p.Ui);
    public static CellStyle HelpBody(CSharpFarPalette p) => PaletteStyles.HelpBody(p.Ui);
    public static CellStyle HelpHeading(CSharpFarPalette p) => PaletteStyles.HelpHeading(p.Ui);
    public static CellStyle HelpKey(CSharpFarPalette p) => PaletteStyles.HelpKey(p.Ui);
    public static CellStyle HelpSeparator(CSharpFarPalette p) => PaletteStyles.HelpSeparator(p.Ui);
    public static CellStyle PathHeaderActive(CSharpFarPalette p) => new(p.PanelPathActiveFg, p.PanelPathActiveBg);
    public static CellStyle CommandLine(CSharpFarPalette p) => new(p.CommandLineFg, p.CommandLineBg);
    public static CellStyle DirectoryShortcutBarNumber(CSharpFarPalette p) => new(p.DirectoryShortcutBarNumberFg, p.DirectoryShortcutBarNumberBg);
    public static CellStyle DirectoryShortcutBarLabel(CSharpFarPalette p) => new(p.DirectoryShortcutBarTextFg, p.DirectoryShortcutBarBg);
    public static CellStyle FileUsageNormal(CSharpFarPalette p) => new(p.FileUsageNormalFg, p.PanelBackground);
    public static CellStyle FileUsageSecondary(CSharpFarPalette p) => new(p.FileUsageSecondaryFg, p.PanelBackground);
    public static CellStyle FileUsageBlocked(CSharpFarPalette p) => new(p.FileUsageBlockedFg, p.PanelBackground, TextAttributes.Bold);
    public static CellStyle FileUsageReasonHeading(CSharpFarPalette p) => new(p.FileUsageReasonHeadingFg, p.PanelBackground, TextAttributes.Bold);
    public static CellStyle FileUsageReasonText(CSharpFarPalette p) => new(p.FileUsageReasonTextFg, p.PanelBackground);
    public static CellStyle FileUsageSelectedOwner(CSharpFarPalette p) => new(p.FileUsageSelectedOwnerFg, p.FileUsageSelectedOwnerBg);
    public static CellStyle FileUsageActionKey(CSharpFarPalette p) => new(p.FileUsageActionKeyFg, p.FileUsageActionKeyBg);
    public static CellStyle FileUsageActionLabel(CSharpFarPalette p) => new(p.FileUsageActionLabelFg, p.FileUsageActionBarBg);
}
