using CSharpFar.Core.Models;
using CSharpFar.App.Rendering;

namespace CSharpFar.App.Commands;

internal static class FileOperationCommandHelpers
{
    public static IReadOnlyList<string> GetOperationSources(
        ApplicationCommandContext context,
        FilePanelState state,
        ApplicationPanelKeyboardFrame? committed)
    {
        if (state.SelectedPaths.Count > 0)
            return [.. state.SelectedPaths];

        FilePanelItem? item = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            state, committed, context.Controller, out var resolvedItem) ? resolvedItem : null;
        if (item is null || item.IsParentDirectory)
            return [];

        return [item.FullPath];
    }

    public static IReadOnlyList<PanelLocation> GetOperationSourceLocations(
        ApplicationCommandContext context,
        FilePanelState state,
        ApplicationPanelKeyboardFrame? committed)
    {
        if (state.SearchRequest is not null)
        {
            var paths = GetOperationSources(context, state, committed);
            return paths.Select(PanelLocation.Local).ToList();
        }

        if (state.SelectedLocations.Count > 0)
            return [.. state.SelectedLocations];

        if (state.SelectedPaths.Count > 0)
        {
            return state.Items
                .Where(item => state.SelectedPaths.Contains(item.FullPath))
                .Select(item => item.Location)
                .ToList();
        }

        FilePanelItem? item = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            state, committed, context.Controller, out var resolvedItem) ? resolvedItem : null;
        if (item is null || item.IsParentDirectory)
            return [];

        return [item.Location];
    }

}
