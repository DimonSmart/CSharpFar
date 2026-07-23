using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public static class WarningDialogStyles
{
    public static CellStyle Fill => new(UiTheme.Current.WarningForeground, UiTheme.Current.WarningBackground);
    public static CellStyle Border => Fill;
    public static CellStyle ButtonFocus => new(UiTheme.Current.WarningButtonFocusedForeground, UiTheme.Current.WarningButtonFocusedBackground);
    public static CellStyle ButtonPressed => new(UiTheme.Current.WarningButtonFocusedBackground, UiTheme.Current.WarningButtonFocusedForeground);
    public static CellStyle Shadow => new(UiTheme.Current.DialogShadowFg, UiTheme.Current.DialogShadowBg);
    public static DialogButtonBarStyle ButtonBar => new(Fill, ButtonFocus, ButtonPressed);

    public static PopupRenderOptions OuterOptions =>
        new()
        {
            DrawBorder = false,
            BackgroundStyle = Fill,
            BorderStyle = Border,
            ShadowStyle = Shadow,
            TitleStyle = Fill,
        };

    public static PopupRenderOptions FrameOptions =>
        new()
        {
            DrawShadow = false,
            BackgroundStyle = Fill,
            BorderStyle = Border,
            ShadowStyle = Shadow,
            TitleStyle = Fill,
        };
}
