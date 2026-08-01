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

    public static CommandCompletionLayoutFrame Calculate(ConsoleSize size, int itemCount)
    {
        int rowCount = Math.Min(VisibleRows(size), itemCount);
        if (rowCount <= 0)
            return CommandCompletionLayoutFrame.Hidden;

        int height = rowCount + 2;
        int commandLineRow = ApplicationLayoutService.CommandLineRow(size);
        var popupBounds = new Rect(0, commandLineRow - height, size.Width, height);
        var contentBounds = new Rect(1, popupBounds.Y + 1, Math.Max(0, popupBounds.Width - 2), rowCount);
        return new CommandCompletionLayoutFrame(
            popupBounds,
            contentBounds,
            new Rect(popupBounds.Right - 1, popupBounds.Y + 1, 1, rowCount),
            rowCount);
    }
}

internal readonly record struct CommandCompletionLayoutFrame(
    Rect PopupBounds,
    Rect ContentBounds,
    Rect CandidateScrollbarBounds,
    int VisibleRows)
{
    public static CommandCompletionLayoutFrame Hidden { get; } = new(default, default, default, 0);
    public bool IsVisible => VisibleRows > 0;
}
