using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public static class FarDialogStyles
{
    public static CellStyle Fill => new(UiTheme.Current.DialogForeground, UiTheme.Current.DialogBackground);
    public static CellStyle Border => new(UiTheme.Current.DialogBorder, UiTheme.Current.DialogBackground);
    public static CellStyle Title => new(UiTheme.Current.DialogTitle, UiTheme.Current.DialogBackground);
    public static CellStyle Input => new(UiTheme.Current.InputText, UiTheme.Current.InputBackground);
    public static CellStyle FocusedInput => new(UiTheme.Current.InputFocusedText, UiTheme.Current.InputFocusedBackground);
    public static CellStyle Error => new(UiTheme.Current.DialogError, UiTheme.Current.DialogBackground);
    public static CellStyle Shadow => new(UiTheme.Current.DialogShadowFg, UiTheme.Current.DialogShadowBg);

    public static PopupRenderOptions OuterOptions =>
        DialogOptions() with
        {
            DrawBorder = false,
        };

    public static PopupRenderOptions FrameOptions =>
        DialogOptions() with
        {
            DrawShadow = false,
        };

    private static PopupRenderOptions DialogOptions() =>
        new()
        {
            BorderStyle = Border,
            BackgroundStyle = Fill,
            ShadowStyle = Shadow,
            TitleStyle = Title,
        };
}
