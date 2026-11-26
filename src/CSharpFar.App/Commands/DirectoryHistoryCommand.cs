using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.Panels;
using CSharpFar.App.Files;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class DirectoryHistoryCommand(PanelCommandTargetResolver targetResolver, DialogService dialogs, IHistoryStore history, FilePanelSourceRegistry sourceRegistry, PanelNavigationService navigation, PanelAutoRefreshService autoRefresh, PanelWorkspaceController workspace, Action resetFunctionKeyLayer) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.DirectoryHistory;

    public bool CanExecute(object? args = null) => true;

    public ApplicationCommandResult Execute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!PanelCommandTargetResolver.CanAccessLocalFileSystem(sourceRegistry, target.State))
            return ApplicationCommandResult.Rendered();

        if (!PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted))
        {
            return ApplicationCommandResult.Rendered();
        }

        try
        {
            var result = dialogs.Select(new SelectionDialogOptions<DirectoryHistoryItem>
            {
                Title = "Directory History",
                Items = history.GetDirectoryHistory().Reverse().ToArray(),
                ItemText = static item => item.Path,
                Presentation = SelectionDialogPresentation.Standard,
            });
            string? path = result.IsConfirmed ? result.SelectedItem?.Path : null;
            if (path is null)
                return ApplicationCommandResult.Rendered();

            if (!LocalPathNormalizer.Current.TryNormalize(
                path,
                basePath: null,
                out string canonicalPath))
            {
                dialogs.Message("Directory History", $"Invalid directory path: {path}");
                return ApplicationCommandResult.Rendered();
            }

            navigation.Navigate(target.State, PanelLocation.Local(canonicalPath), preserveOnError: true, loaded =>
            {
                if (loaded)
                    autoRefresh.StartWatching(target.State, workspace.PanelSideForState(target.State));
                else
                    dialogs.Message("Directory History", $"Cannot open directory: {canonicalPath}");
            }, requireExistingLocalDirectory: true);

            return ApplicationCommandResult.Rendered();
        }
        finally
        {
            resetFunctionKeyLayer();
        }
    }
}
