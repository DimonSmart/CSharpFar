using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Ui;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.Panels;
using CSharpFar.App.Files;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Editor;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class OpenCreateFileCommand(PanelCommandTargetResolver targetResolver, PanelController controller, PanelFileViewerService viewer, PanelRefreshService panelRefresh, PanelWorkspaceController workspace, DialogService dialogs, FormFieldFactory fields, AppSettingsAlias settings, IHistoryStore history, FilePanelSourceRegistry sourceRegistry) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.OpenCreateFile;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        FileOperationCommandHelpers.HasCapability(targetResolver.ResolvePanelTarget(args).State, PanelProviderCapabilities.CreateFile);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
        {
            FileOperationCommandHelpers.ShowReadOnlyPanelMessage(dialogs, "Create file");
            return ApplicationCommandResult.Rendered();
        }

        if (!PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted))
            return ApplicationCommandResult.Rendered();

        var dialog = new OpenCreateFileDialog(dialogs, fields);
        var result = dialog.Show(
            InitialPath(target),
            attempt => ValidateLocalPath(target.State.SourcePath, attempt));

        if (result is null)
            return ApplicationCommandResult.Rendered();

        string filePath;
        PanelLocation fileLocation;
        try
        {
            fileLocation = ResolvePath(target.State, result.FilePath);
            filePath = fileLocation.SourcePath;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            dialogs.Message("Editor", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        EditorDocumentFormat newFileFormat = result.CodePage.CreateDocumentFormat(settings.Editor);
        var outcome = viewer.EditFileWithNewFileFormat(
            fileLocation, newFileFormat, PanelCommandEditorContextFactory.Create(controller, target));
        if (fileLocation.SourceId == PanelSourceId.Local && outcome.ExistsAfter)
            history.AddFile(new FileHistoryItem { Path = filePath });

        if (outcome.ExistsAfter && !outcome.ExistedBefore)
            panelRefresh.SafeRefresh(target.State, target.VisibleRows, loaded =>
            {
                if (loaded) controller.SetCursorByLocation(target.State, fileLocation, workspace.VisibleRows(workspace.PanelSideForState(target.State)));
            });
        else
            panelRefresh.SafeRefresh(target.State, target.VisibleRows);

        return ApplicationCommandResult.Rendered();
    }

    private string? InitialPath(ResolvedPanelCommandTarget target)
    {
        FilePanelItem? item = PanelCommandTargetResolver.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, controller, out var resolvedItem) ? resolvedItem : null;
        return item is { IsDirectory: false, IsParentDirectory: false }
            ? item.Name
            : null;
    }


    private static string? ValidateLocalPath(string currentDirectory, string attempt)
    {
        if (attempt.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return "Invalid characters in file path.";

        try
        {
            _ = ResolveLocalPath(currentDirectory, attempt);
            return null;
        }
        catch (ArgumentException ex) { return ex.Message; }
        catch (NotSupportedException ex) { return ex.Message; }
        catch (PathTooLongException ex) { return ex.Message; }
    }

    private PanelLocation ResolvePath(
        FilePanelState state,
        string path)
    {
        if (state.SourceId == PanelSourceId.Local)
            return PanelLocation.Local(ResolveLocalPath(state.SourcePath, path));

        string combined = path.Contains('/') || path.Contains('\\')
            ? path
            : FileOperationCommandHelpers.CombinePanelPath(sourceRegistry, state, path);
        return new PanelLocation(state.SourceId, combined.Replace('\\', '/'));
    }

    private static string ResolveLocalPath(string currentDirectory, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(currentDirectory, path));

    private static bool IsInCurrentLocalDirectory(string currentDirectory, string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (directory is null)
            return false;

        return string.Equals(
            Path.GetFullPath(currentDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
