using System.Diagnostics;
using System.Runtime.InteropServices;
using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class OpenFileAttributesCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Attributes;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (target.State.SourceId != PanelSourceId.Local)
        {
            new MessageDialog(context.ModalDialogs).Show("Attributes", "File attributes are supported only for local files.");
            return ApplicationCommandResult.Rendered();
        }

        var targets = GetTargetItems(context, target);
        if (targets.Count == 0)
            return ApplicationCommandResult.Rendered();

        var paths = targets.Select(static item => item.FullPath).ToList();
        FileMetadataSnapshot snapshot;
        try
        {
            snapshot = paths.Count == 1
                ? context.FileMetadata.GetMetadata(paths[0])
                : context.FileMetadata.GetMergedMetadata(paths);
        }
        catch (Exception ex)
        {
            new MessageDialog(context.ModalDialogs).Show("Attributes Error", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        var dialog = context.CreateFileAttributesDialog();
        var result = dialog.Show(snapshot);
        if (result is null)
            return ApplicationCommandResult.Rendered();

        if (result.OpenSystemProperties)
        {
            OpenSystemProperties(paths[0], context);
            return ApplicationCommandResult.Rendered();
        }

        if (!HasChanges(result.ChangeSet))
            return ApplicationCommandResult.Rendered();

        FileMetadataApplyResult applyResult = context.FileMetadata.ApplyMetadata(paths, result.ChangeSet);
        if (applyResult.Errors.Count > 0)
            ShowApplyErrors(context, applyResult);

        context.RefreshPanels();
        return ApplicationCommandResult.Rendered();
    }

    private static IReadOnlyList<FilePanelItem> GetTargetItems(
        ApplicationCommandContext context,
        ResolvedPanelCommandTarget target)
    {
        if (target.State.SelectedPaths.Count > 0)
        {
            return target.State.Items
                .Where(item => !item.IsParentDirectory && target.State.SelectedPaths.Contains(item.FullPath))
                .ToList();
        }

        FilePanelItem? item = target.Committed is { } committed
            ? ApplicationCommandContext.TryResolveCommittedCurrentItem(target.State, committed, out var committedItem)
                ? committedItem
                : null
            : context.Controller.CurrentItem(target.State);
        return item is null || item.IsParentDirectory ? [] : [item];
    }

    private static bool HasChanges(FileMetadataChangeSet changeSet) =>
        changeSet.AttributeChanges.Count > 0 ||
        changeSet.CreationTime is not null ||
        changeSet.LastWriteTime is not null ||
        changeSet.LastAccessTime is not null;

    private static void ShowApplyErrors(ApplicationCommandContext context, FileMetadataApplyResult result)
    {
        string details = string.Join(
            Environment.NewLine,
            result.Errors.Select(error => $"{Path.GetFileName(error.Path)}: {error.Message}"));
        string message = $"Changed: {result.ChangedCount}{Environment.NewLine}Failed: {result.Errors.Count}{Environment.NewLine}{Environment.NewLine}{details}";
        new MessageDialog(context.ModalDialogs).Show("Attributes Error", message);
    }

    private static void OpenSystemProperties(string path, ApplicationCommandContext context)
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
            new MessageDialog(context.ModalDialogs).Show("System Properties", ex.Message);
        }
    }
}
