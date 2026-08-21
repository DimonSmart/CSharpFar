using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class EditFileCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Edit;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.Edit);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
        {
            context.ShowReadOnlyPanelMessage("Edit");
            return ApplicationCommandResult.Rendered();
        }

        FilePanelItem? item = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, context.Controller, out var resolvedItem) ? resolvedItem : null;
        if (item is null || item.IsParentDirectory)
            return ApplicationCommandResult.Rendered();

        if (item.IsDirectory)
            return ApplicationCommandResult.Rendered();

        context.EditPanelFile(target.State, item);
        if (ShouldRefreshPanelAfterEdit(target.State))
            context.SafeRefresh(target.State, target.VisibleRows);

        return ApplicationCommandResult.Rendered();
    }

    internal static bool ShouldRefreshPanelAfterEdit(FilePanelState state) =>
        state.SearchRequest is null;
}
