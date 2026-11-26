using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Services;
using CSharpFar.App.Viewer;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class OpenFileAttributesCommand(PanelCommandTargetResolver targetResolver, PanelController controller, DialogService dialogs, IFileMetadataService fileMetadata, Func<IFileAttributesDialog> fileAttributesDialogFactory, PanelRefreshService panelRefresh, PanelSessionState panels) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Attributes;

    public bool CanExecute(object? args = null) => true;

    public ApplicationCommandResult Execute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (target.State.SourceId != PanelSourceId.Local)
        {
            dialogs.Message("Attributes", "File attributes are supported only for local files.");
            return ApplicationCommandResult.Rendered();
        }

        var targets = GetTargetItems(target);
        if (targets.Count == 0 || targets.Any(item => item.SourceId != PanelSourceId.Local))
            return ApplicationCommandResult.Rendered();

        var paths = targets.Select(static item => item.FullPath).ToList();
        FileMetadataSnapshot snapshot;
        try
        {
            snapshot = paths.Count == 1
                ? fileMetadata.GetMetadata(paths[0])
                : fileMetadata.GetMergedMetadata(paths);
        }
        catch (Exception ex)
        {
            dialogs.Message("Attributes Error", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        var dialog = fileAttributesDialogFactory();
        var result = dialog.Show(snapshot);
        if (result is null)
            return ApplicationCommandResult.Rendered();

        if (result.OpenSystemProperties)
        {
            OpenSystemProperties(paths[0]);
            return ApplicationCommandResult.Rendered();
        }

        if (!HasChanges(result.ChangeSet))
            return ApplicationCommandResult.Rendered();

        FileMetadataApplyResult applyResult = fileMetadata.ApplyMetadata(paths, result.ChangeSet);
        if (applyResult.Errors.Count > 0)
            ShowApplyErrors(applyResult);

        panelRefresh.RefreshPanels(panels.Left, panels.Right);
        return ApplicationCommandResult.Rendered();
    }

    private IReadOnlyList<FilePanelItem> GetTargetItems(
        ResolvedPanelCommandTarget target)
    {
        if (target.State.SelectedLocations.Count > 0)
        {
            return target.State.Items
                .Where(item => !item.IsParentDirectory && target.State.SelectedLocations.Contains(item.Location))
                .ToList();
        }

        FilePanelItem? item = PanelCommandTargetResolver.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, controller, out var resolvedItem) ? resolvedItem : null;
        return item is null || item.IsParentDirectory ? [] : [item];
    }

    private static bool HasChanges(FileMetadataChangeSet changeSet) =>
        changeSet.AttributeChanges.Count > 0 ||
        changeSet.CreationTime is not null ||
        changeSet.LastWriteTime is not null ||
        changeSet.LastAccessTime is not null;

    private void ShowApplyErrors(FileMetadataApplyResult result)
    {
        string details = string.Join(
            Environment.NewLine,
            result.Errors.Select(error => $"{Path.GetFileName(error.Path)}: {error.Message}"));
        string message = $"Changed: {result.ChangedCount}{Environment.NewLine}Failed: {result.Errors.Count}{Environment.NewLine}{Environment.NewLine}{details}";
        dialogs.Message("Attributes Error", message);
    }

    private void OpenSystemProperties(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "properties",
            });
        }
        catch (Exception ex)
        {
            dialogs.Message("System Properties", ex.Message);
        }
    }
}
