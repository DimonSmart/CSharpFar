using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class Theme
{
    // --- Active panel ---
    public static readonly CellStyle PanelBorderActive   = new(ConsoleColor.White,     ConsoleColor.DarkBlue);
    public static readonly CellStyle PanelFillActive     = new(ConsoleColor.White,     ConsoleColor.DarkBlue);
    public static readonly CellStyle FileActive          = new(ConsoleColor.White,     ConsoleColor.DarkBlue);
    public static readonly CellStyle DirectoryActive     = new(ConsoleColor.Cyan,      ConsoleColor.DarkBlue);
    public static readonly CellStyle CursorActive        = new(ConsoleColor.Black,     ConsoleColor.Cyan);
    public static readonly CellStyle PathHeaderActive    = new(ConsoleColor.Black,     ConsoleColor.Cyan);
    public static readonly CellStyle FooterActive        = new(ConsoleColor.DarkCyan,  ConsoleColor.DarkBlue);

    // --- Inactive panel ---
    public static readonly CellStyle PanelBorderInactive = new(ConsoleColor.DarkGray,  ConsoleColor.DarkBlue);
    public static readonly CellStyle PanelFillInactive   = new(ConsoleColor.Gray,      ConsoleColor.DarkBlue);
    public static readonly CellStyle FileInactive        = new(ConsoleColor.Gray,      ConsoleColor.DarkBlue);
    public static readonly CellStyle DirectoryInactive   = new(ConsoleColor.DarkCyan,  ConsoleColor.DarkBlue);
    public static readonly CellStyle CursorInactive      = new(ConsoleColor.Black,     ConsoleColor.DarkGray);
    public static readonly CellStyle PathHeaderInactive  = new(ConsoleColor.DarkGray,  ConsoleColor.DarkBlue);
    public static readonly CellStyle FooterInactive      = new(ConsoleColor.DarkGray,  ConsoleColor.DarkBlue);

    // --- Command line ---
    public static readonly CellStyle CommandLine         = new(ConsoleColor.White,     ConsoleColor.Black);

    // --- Key bar ---
    public static readonly CellStyle KeyBarNum           = new(ConsoleColor.Black,     ConsoleColor.DarkCyan);
    public static readonly CellStyle KeyBarLabel         = new(ConsoleColor.Black,     ConsoleColor.DarkGray);
}
