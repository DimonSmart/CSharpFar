using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Ui;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.Files;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Panels;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class ViewFileCommand(PanelCommandTargetResolver targetResolver, PanelController controller, PanelFileViewerService viewer, PanelDirectorySizeCoordinator? directorySizes = null) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.View;

    public bool CanExecute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        FilePanelItem? item = ResolveItem(target);
        if (item is null || item.IsParentDirectory)
            return false;

        return item.IsDirectory
            ? directorySizes?.CanCalculate(target.State, item) == true
            : FileOperationCommandHelpers.HasCapability(target.State, PanelProviderCapabilities.OpenRead);
    }

    public ApplicationCommandResult Execute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted))
            return ApplicationCommandResult.Rendered();

        FilePanelItem? item = ResolveItem(target);
        if (item is null || item.IsParentDirectory)
            return ApplicationCommandResult.Rendered();

        if (item.IsDirectory)
        {
            if (directorySizes?.CanCalculate(target.State, item) == true)
                directorySizes.Calculate(target.Side, target.State, item);
            return ApplicationCommandResult.Rendered();
        }

        if (FileOperationCommandHelpers.HasCapability(target.State, PanelProviderCapabilities.OpenRead))
            viewer.ViewPanelFile(target.State, item);
        return ApplicationCommandResult.Rendered();
    }

    private FilePanelItem? ResolveItem(
        ResolvedPanelCommandTarget target) =>
        PanelCommandTargetResolver.TryResolveCommittedCurrentItem(
            target.State,
            target.ActiveCommitted,
            controller,
            out var resolvedItem)
            ? resolvedItem
            : null;
}
