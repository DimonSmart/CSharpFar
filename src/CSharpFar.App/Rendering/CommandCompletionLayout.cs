using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class CommandCompletionLayout
{
    public static CommandCompletionLayoutFrame Calculate(ConsoleSize size, int itemCount)
    {
        Rect? popupBounds = CommandCompletionLayer.CalculatePopupBounds(size, itemCount);
        return popupBounds is { } bounds
            ? new CommandCompletionLayoutFrame(bounds)
            : CommandCompletionLayoutFrame.Hidden;
    }
}

internal readonly record struct CommandCompletionLayoutFrame(Rect PopupBounds)
{
    public static CommandCompletionLayoutFrame Hidden { get; } = new(default);
    public bool IsVisible => PopupBounds.Width > 0 && PopupBounds.Height > 0;
}
