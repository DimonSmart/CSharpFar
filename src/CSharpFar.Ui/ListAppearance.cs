using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public enum ListAppearance { Dialog, Menu }

public readonly record struct ListAppearanceStyles(
    CellStyle Header,
    CellStyle Border,
    CellStyle Normal,
    CellStyle Selected,
    CellStyle Emphasized,
    CellStyle SelectedEmphasized,
    CellStyle Scrollbar)
{
    public static ListAppearanceStyles From(ListAppearance appearance)
    {
        ConsolePalette palette = UiTheme.Current;
        return appearance == ListAppearance.Menu
            ? new(
                new(palette.MenuNormalFg, palette.MenuNormalBg),
                new(palette.MenuBorderFg, palette.MenuBorderBg),
                new(palette.MenuNormalFg, palette.MenuNormalBg),
                new(palette.MenuActiveFg, palette.MenuActiveBg),
                new(palette.MenuHighlightFg, palette.MenuHighlightBg),
                new(palette.MenuActiveHighlightFg, palette.MenuActiveHighlightBg),
                new(palette.MenuBorderFg, palette.MenuBorderBg))
            : new(
                FarDialogStyles.Title,
                FarDialogStyles.Border,
                FarDialogStyles.Fill,
                FarDialogStyles.FocusedInput,
                FarDialogStyles.Title,
                FarDialogStyles.FocusedInput,
                FarDialogStyles.Border);
    }
}
