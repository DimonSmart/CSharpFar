using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public static class FarDialogStyles
{
    public static CellStyle Fill { get; } = new(ConsoleColor.Black, ConsoleColor.Gray);
    public static CellStyle Border { get; } = new(ConsoleColor.DarkGray, ConsoleColor.Gray);
    public static CellStyle Title { get; } = new(ConsoleColor.Black, ConsoleColor.Gray);
    public static CellStyle Input { get; } = new(ConsoleColor.White, ConsoleColor.DarkCyan);
    public static CellStyle FocusedInput { get; } = new(ConsoleColor.White, ConsoleColor.DarkBlue);
    public static CellStyle Error { get; } = new(ConsoleColor.Yellow, ConsoleColor.Gray);
    public static CellStyle Shadow { get; } = new(ConsoleColor.Black, ConsoleColor.Black);

    public static PopupRenderOptions OuterOptions { get; } =
        new()
        {
            DrawBorder = false,
            BorderStyle = Border,
            BackgroundStyle = Fill,
            ShadowStyle = Shadow,
            TitleStyle = Title,
        };

    public static PopupRenderOptions FrameOptions { get; } =
        new()
        {
            DrawShadow = false,
            BorderStyle = Border,
            BackgroundStyle = Fill,
            ShadowStyle = Shadow,
            TitleStyle = Title,
        };
}
