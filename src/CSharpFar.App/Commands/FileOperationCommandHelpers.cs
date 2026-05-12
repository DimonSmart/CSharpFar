using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class FileOperationCommandHelpers
{
    public static IReadOnlyList<string> GetOperationSources(ApplicationCommandContext context)
    {
        if (context.ActiveState.SelectedPaths.Count > 0)
            return [.. context.ActiveState.SelectedPaths];

        var item = context.Controller.CurrentItem(context.ActiveState);
        if (item is null || item.IsParentDirectory)
            return [];

        return [item.FullPath];
    }

    public static ScreenSnapshot CaptureScreen(ApplicationCommandContext context)
    {
        var size = context.Screen.GetSize();
        return context.Screen.Capture(new Rect(0, 0, size.Width, size.Height));
    }
}
