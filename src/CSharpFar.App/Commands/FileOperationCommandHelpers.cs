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

    public static IReadOnlyList<PanelLocation> GetOperationSourceLocations(ApplicationCommandContext context)
    {
        if (context.ActiveState.SearchRequest is not null)
        {
            var paths = GetOperationSources(context);
            return paths.Select(PanelLocation.Local).ToList();
        }

        if (context.ActiveState.SelectedLocations.Count > 0)
            return [.. context.ActiveState.SelectedLocations];

        if (context.ActiveState.SelectedPaths.Count > 0)
        {
            return context.ActiveState.Items
                .Where(item => context.ActiveState.SelectedPaths.Contains(item.FullPath))
                .Select(item => item.Location)
                .ToList();
        }

        var item = context.Controller.CurrentItem(context.ActiveState);
        if (item is null || item.IsParentDirectory)
            return [];

        return [item.Location];
    }

    public static ScreenSnapshot CaptureScreen(ApplicationCommandContext context)
    {
        var size = context.Screen.GetSize();
        return context.Screen.Capture(new Rect(0, 0, size.Width, size.Height));
    }
}
