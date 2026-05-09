using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class WarningDialogStyles
{
    public static CellStyle Fill => new(ConsoleColor.White, ConsoleColor.DarkRed);
    public static CellStyle Border => new(ConsoleColor.White, ConsoleColor.DarkRed);
    public static CellStyle ButtonFocus => new(ConsoleColor.Black, ConsoleColor.Gray);
    public static CellStyle Shadow => new(ConsoleColor.Black, ConsoleColor.Black);

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
