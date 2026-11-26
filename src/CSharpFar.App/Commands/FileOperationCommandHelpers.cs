using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class FileOperationCommandHelpers
{
    public static IReadOnlyList<string> GetOperationSources(
        ApplicationCommandContext context,
        FilePanelState state,
        ApplicationPanelKeyboardFrame? committed) =>
        GetOperationSourceLocations(context, state, committed).Select(location => location.SourcePath).ToArray();

    public static IReadOnlyList<PanelLocation> GetOperationSourceLocations(
        ApplicationCommandContext context,
        FilePanelState state,
        ApplicationPanelKeyboardFrame? committed)
    {
        if (state.SelectedLocations.Count > 0)
            return [.. state.SelectedLocations];

        FilePanelItem? item = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            state, committed, context.Controller, out var resolvedItem) ? resolvedItem : null;
        return item is null || item.IsParentDirectory ? [] : [item.Location];
    }

}
