using System.Threading;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public static class DialogStyles
{
    private static readonly AsyncLocal<DialogAppearance?> s_appearance = new();

    private static bool IsWarning => s_appearance.Value == DialogAppearance.Warning;

    public static CellStyle Fill => IsWarning
        ? WarningDialogStyles.Fill
        : new(UiTheme.Current.DialogForeground, UiTheme.Current.DialogBackground);
    public static CellStyle Border => IsWarning
        ? WarningDialogStyles.Border
        : new(UiTheme.Current.DialogBorder, UiTheme.Current.DialogBackground);
    public static CellStyle Title => IsWarning
        ? WarningDialogStyles.Fill
        : new(UiTheme.Current.DialogTitle, UiTheme.Current.DialogBackground);
    public static CellStyle Input => IsWarning
        ? WarningDialogStyles.Fill
        : new(UiTheme.Current.InputText, UiTheme.Current.InputBackground);
    public static CellStyle FocusedInput => IsWarning
        ? WarningDialogStyles.ButtonFocus
        : new(UiTheme.Current.InputFocusedText, UiTheme.Current.InputFocusedBackground);
    public static CellStyle DisabledControl(CellStyle backgroundSource) =>
        new(UiTheme.Current.DisabledControlForeground, backgroundSource.Background);
    public static CellStyle PressedButton => IsWarning
        ? WarningDialogStyles.ButtonPressed
        : new(UiTheme.Current.InputFocusedBackground, UiTheme.Current.InputFocusedText);
    internal static DialogButtonBarStyle ButtonBar => IsWarning
        ? WarningDialogStyles.ButtonBar
        : new(Fill, FocusedInput, PressedButton);
    public static CellStyle Error => IsWarning
        ? WarningDialogStyles.Error
        : new(UiTheme.Current.DialogError, UiTheme.Current.DialogBackground);
    public static CellStyle Shadow => IsWarning
        ? WarningDialogStyles.Shadow
        : new(UiTheme.Current.DialogShadowFg, UiTheme.Current.DialogShadowBg);

    public static PopupRenderOptions PopupOptions => DialogOptions();

    internal static IDisposable UseAppearance(DialogAppearance appearance)
    {
        DialogAppearance? previous = s_appearance.Value;
        s_appearance.Value = appearance;
        return new AppearanceScope(previous);
    }

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

    private sealed class AppearanceScope(DialogAppearance? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            s_appearance.Value = previous;
            _disposed = true;
        }
    }
}
