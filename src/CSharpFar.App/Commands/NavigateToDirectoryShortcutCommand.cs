using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Ui;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.UserMenu;
using CSharpFar.App.State;
using CSharpFar.App.Rendering;
using CSharpFar.App.Panels;
using CSharpFar.App.Modules;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Dialogs;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.App.Commands;

internal sealed record NavigateToDirectoryShortcutArgs(int Number);

internal sealed record NavigateToCommittedDirectoryShortcutArgs(
    int Number,
    string Path,
    PanelSide Side);

internal sealed class NavigateToDirectoryShortcutCommand(PanelWorkspaceController workspace, FilePanelSourceRegistry sourceRegistry, AppSettingsAlias settings, DialogService dialogs, PanelNavigationService navigation, PanelAutoRefreshService autoRefresh, Action resetTransientNavigationUi) : IApplicationCommand
{
    public string CommandId => DirectoryShortcutCommandIds.Navigate;

    public bool CanExecute(object? args = null) =>
        args switch
        {
            NavigateToDirectoryShortcutArgs menu =>
                workspace.IsPanelsMode &&
                PanelCommandTargetResolver.CanAccessLocalFileSystem(sourceRegistry, workspace.ActiveState) &&
                DirectoryShortcutNormalizer.IsValidNumber(menu.Number),
            NavigateToCommittedDirectoryShortcutArgs committed =>
                PanelCommandTargetResolver.CanAccessLocalFileSystem(sourceRegistry, workspace.GetPanelState(committed.Side)) &&
                DirectoryShortcutNormalizer.IsValidNumber(committed.Number),
            _ => false,
        };

    public ApplicationCommandResult Execute(object? args = null)
    {
        if (!CanExecute(args))
            return ApplicationCommandResult.Rendered();

        (string? path, PanelSide side) = args switch
        {
            NavigateToDirectoryShortcutArgs menu => (
                DirectoryShortcutNormalizer.Normalize(settings.DirectoryShortcuts)
                    .SingleOrDefault(candidate => candidate.Number == menu.Number)?.Path,
                workspace.ActiveSide),
            NavigateToCommittedDirectoryShortcutArgs committed => (committed.Path, committed.Side),
            _ => throw new InvalidOperationException(),
        };

        if (path is null)
            return ApplicationCommandResult.Rendered();

        FilePanelState state = workspace.GetPanelState(side);
        if (!PanelCommandTargetResolver.CanAccessLocalFileSystem(sourceRegistry, state))
            return ApplicationCommandResult.Rendered();

        if (!LocalPathNormalizer.Current.TryNormalize(
            path,
            basePath: null,
            out string canonicalPath))
        {
            dialogs.Message("Directory Shortcut", $"Invalid directory path: {path}");
            return ApplicationCommandResult.Rendered();
        }

        resetTransientNavigationUi();
        navigation.Navigate(state, PanelLocation.Local(canonicalPath), preserveOnError: true, loaded =>
        {
            if (loaded)
                autoRefresh.StartWatching(state, workspace.PanelSideForState(state));
            else
                dialogs.Message("Directory Shortcut", $"Cannot open directory: {canonicalPath}");
        }, requireExistingLocalDirectory: true);

        return ApplicationCommandResult.Rendered();
    }
}
