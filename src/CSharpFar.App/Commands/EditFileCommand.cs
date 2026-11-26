using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Ui;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.Panels;
using CSharpFar.App.Files;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class EditFileCommand(PanelCommandTargetResolver targetResolver, PanelController controller, PanelFileViewerService viewer, PanelRefreshService panelRefresh, DialogService dialogs) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Edit;

    public bool CanExecute(ApplicationCommandContext context, object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        return PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted) &&
            FileOperationCommandHelpers.HasCapability(target.State, PanelProviderCapabilities.Edit) &&
            PanelCommandTargetResolver.TryResolveCommittedCurrentItem(
                target.State, target.ActiveCommitted, controller, out var item) &&
            !item.IsParentDirectory &&
            !item.IsDirectory;
    }

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted))
            return ApplicationCommandResult.Rendered();

        FilePanelItem? item = PanelCommandTargetResolver.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, controller, out var resolvedItem) ? resolvedItem : null;
        if (item is null || item.IsParentDirectory || item.IsDirectory)
            return ApplicationCommandResult.Rendered();

        if (!FileOperationCommandHelpers.HasCapability(target.State, PanelProviderCapabilities.Edit))
        {
            FileOperationCommandHelpers.ShowReadOnlyPanelMessage(dialogs, "Edit");
            return ApplicationCommandResult.Rendered();
        }

        viewer.EditPanelFile(target.State, item);
        if (ShouldRefreshPanelAfterEdit(target.State))
            panelRefresh.SafeRefresh(target.State, target.VisibleRows);

        return ApplicationCommandResult.Rendered();
    }

    internal static bool ShouldRefreshPanelAfterEdit(FilePanelState state) =>
        state.SearchRequest is null;
}
