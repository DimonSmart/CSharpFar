using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class CommandCompletionLayout
{
    public const int MaxVisibleRows = 8;

    public static int VisibleRows(ConsoleSize size)
    {
        int rowsAboveCommandLine = ApplicationLayoutService.CommandLineRow(size) - 2;
        return Math.Max(0, Math.Min(MaxVisibleRows, rowsAboveCommandLine));
    }
}
