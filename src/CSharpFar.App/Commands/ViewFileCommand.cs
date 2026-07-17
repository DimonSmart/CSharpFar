using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class ViewFileCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.View;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.OpenRead);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
            return ApplicationCommandResult.Rendered();

        FilePanelItem? item = target.Committed is { } committed
            ? ApplicationCommandContext.TryResolveCommittedCurrentItem(target.State, committed, out var committedItem)
                ? committedItem
                : null
            : context.Controller.CurrentItem(target.State);
        if (item is null || item.IsParentDirectory || item.IsDirectory)
            return ApplicationCommandResult.Rendered();

        context.ViewPanelFile(target.State, item);
        return ApplicationCommandResult.Rendered();
    }
}
