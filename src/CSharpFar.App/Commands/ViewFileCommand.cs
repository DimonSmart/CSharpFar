using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Panels;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class ViewFileCommand : IApplicationCommand
{
    private readonly PanelDirectorySizeCoordinator? _directorySizes;

    public ViewFileCommand(PanelDirectorySizeCoordinator? directorySizes = null) =>
        _directorySizes = directorySizes;

    public string CommandId => FunctionKeyCommandIds.View;

    public bool CanExecute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        FilePanelItem? item = ResolveItem(context, target);
        if (item is null || item.IsParentDirectory)
            return false;

        return item.IsDirectory
            ? _directorySizes?.CanCalculate(target.State, item) == true
            : context.HasCapability(target.State, PanelProviderCapabilities.OpenRead);
    }

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted))
            return ApplicationCommandResult.Rendered();

        FilePanelItem? item = ResolveItem(context, target);
        if (item is null || item.IsParentDirectory)
            return ApplicationCommandResult.Rendered();

        if (item.IsDirectory)
        {
            if (_directorySizes?.CanCalculate(target.State, item) == true)
                _directorySizes.Calculate(target.Side, target.State, item);
            return ApplicationCommandResult.Rendered();
        }

        if (context.HasCapability(target.State, PanelProviderCapabilities.OpenRead))
            context.ViewPanelFile(target.State, item);
        return ApplicationCommandResult.Rendered();
    }

    private static FilePanelItem? ResolveItem(
        ApplicationCommandContext context,
        ResolvedPanelCommandTarget target) =>
        ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.State,
            target.ActiveCommitted,
            context.Controller,
            out var resolvedItem)
            ? resolvedItem
            : null;
}
