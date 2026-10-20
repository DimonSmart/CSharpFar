using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class EditFileCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Edit;

    public bool CanExecute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        return ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted) &&
            context.HasCapability(target.State, PanelProviderCapabilities.Edit) &&
            ApplicationCommandContext.TryResolveCommittedCurrentItem(
                target.State, target.ActiveCommitted, context.Controller, out var item) &&
            !item.IsParentDirectory &&
            !item.IsDirectory;
    }

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted))
            return ApplicationCommandResult.Rendered();

        FilePanelItem? item = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, context.Controller, out var resolvedItem) ? resolvedItem : null;
        if (item is null || item.IsParentDirectory || item.IsDirectory)
            return ApplicationCommandResult.Rendered();

        if (!context.HasCapability(target.State, PanelProviderCapabilities.Edit))
        {
            context.ShowReadOnlyPanelMessage("Edit");
            return ApplicationCommandResult.Rendered();
        }

        context.EditPanelFile(target.State, item);
        if (ShouldRefreshPanelAfterEdit(target.State))
            context.SafeRefresh(target.State, target.VisibleRows);

        return ApplicationCommandResult.Rendered();
    }

    internal static bool ShouldRefreshPanelAfterEdit(FilePanelState state) =>
        state.SearchRequest is null;
}
