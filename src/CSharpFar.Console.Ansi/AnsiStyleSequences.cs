using System.Text;
using CSharpFar.Console.Models;

namespace CSharpFar.Console.Ansi;

internal static class AnsiStyleSequences
{
    public static string BuildSgr(
        ConsoleColor foreground,
        ConsoleColor background,
        TextAttributes attributes)
    {
        var builder = new StringBuilder();
        builder.Append("\x1b[0");

        if (attributes.HasFlag(TextAttributes.Bold))
            builder.Append(";1");
        if (attributes.HasFlag(TextAttributes.Underline))
            builder.Append(";4");
        if (attributes.HasFlag(TextAttributes.Reverse))
            builder.Append(";7");

        builder.Append(';');
        builder.Append(ForegroundCode(foreground));
        builder.Append(';');
        builder.Append(BackgroundCode(background));
        builder.Append('m');

        return builder.ToString();
    }

    public static int ForegroundCode(ConsoleColor color) =>
        color switch
        {
            ConsoleColor.Black => 30,
            ConsoleColor.DarkRed => 31,
            ConsoleColor.DarkGreen => 32,
            ConsoleColor.DarkYellow => 33,
            ConsoleColor.DarkBlue => 34,
            ConsoleColor.DarkMagenta => 35,
            ConsoleColor.DarkCyan => 36,
            ConsoleColor.Gray => 37,
            ConsoleColor.DarkGray => 90,
            ConsoleColor.Red => 91,
            ConsoleColor.Green => 92,
            ConsoleColor.Yellow => 93,
            ConsoleColor.Blue => 94,
            ConsoleColor.Magenta => 95,
            ConsoleColor.Cyan => 96,
            ConsoleColor.White => 97,
            _ => 37,
        };

    public static int BackgroundCode(ConsoleColor color) =>
        color switch
        {
            ConsoleColor.Black => 40,
            ConsoleColor.DarkRed => 41,
            ConsoleColor.DarkGreen => 42,
            ConsoleColor.DarkYellow => 43,
            ConsoleColor.DarkBlue => 44,
            ConsoleColor.DarkMagenta => 45,
            ConsoleColor.DarkCyan => 46,
            ConsoleColor.Gray => 47,
            ConsoleColor.DarkGray => 100,
            ConsoleColor.Red => 101,
            ConsoleColor.Green => 102,
            ConsoleColor.Yellow => 103,
            ConsoleColor.Blue => 104,
            ConsoleColor.Magenta => 105,
            ConsoleColor.Cyan => 106,
            ConsoleColor.White => 107,
            _ => 40,
        };
}
