using CSharpFar.Ui;
using CSharpFar.App.Dialogs;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Services;
using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class FileOperationCommandHelpers
{
    public static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;

    public static void ShowReadOnlyPanelMessage(DialogService dialogs, string action) =>
        dialogs.Message(action, "The current panel source does not support this operation.");

    public static string CombinePanelPath(FilePanelSourceRegistry sourceRegistry, FilePanelState state, string name) =>
        sourceRegistry.GetSource(state.SourceId).CombinePath(state.SourcePath, name);

    public static string BindLocalDestination(
        FilePanelState destinationState,
        IReadOnlyList<PanelLocation> sources,
        FileOperationDialogResult result)
    {
        string destination = result.Destination;
        if (destinationState.SourceId != PanelSourceId.Local ||
            sources.All(source => source.SourceId == PanelSourceId.Local) ||
            Path.IsPathFullyQualified(destination))
            return destination;

        if (Path.IsPathRooted(destination))
            throw new IOException("Use a fully qualified local destination path.");

        // A remote source has no local parent. Anchor its local destination
        // to the local destination panel, without evaluating the expression.
        string parent = destinationState.SourcePath;
        if (result.UseDestinationTemplate)
            parent = parent.Replace("{", "{{", StringComparison.Ordinal)
                .Replace("}", "}}", StringComparison.Ordinal);
        return Path.Combine(parent, destination);
    }

    public static IReadOnlyList<string> GetOperationSources(
        PanelController controller,
        FilePanelState state,
        ApplicationPanelKeyboardFrame? committed) =>
        GetOperationSourceLocations(controller, state, committed).Select(location => location.SourcePath).ToArray();

    public static IReadOnlyList<PanelLocation> GetOperationSourceLocations(
        PanelController controller,
        FilePanelState state,
        ApplicationPanelKeyboardFrame? committed)
    {
        if (state.SelectedLocations.Count > 0)
            return [.. state.SelectedLocations];

        FilePanelItem? item = PanelCommandTargetResolver.TryResolveCommittedCurrentItem(
            state, committed, controller, out var resolvedItem) ? resolvedItem : null;
        return item is null || item.IsParentDirectory ? [] : [item.Location];
    }

}
