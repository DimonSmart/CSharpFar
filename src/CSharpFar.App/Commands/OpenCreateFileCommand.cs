using CSharpFar.App.Dialogs;
using CSharpFar.App.Editor;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class OpenCreateFileCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.OpenCreateFile;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.CreateFile);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
        {
            context.ShowReadOnlyPanelMessage("Create file");
            return ApplicationCommandResult.Rendered();
        }

        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted))
            return ApplicationCommandResult.Rendered();

        var dialog = new OpenCreateFileDialog(context.ModalDialogs, context.TextFieldHistory);
        var result = dialog.Show(
            InitialPath(context, target),
            attempt => ValidateLocalPath(target.State.SourcePath, attempt));

        if (result is null)
            return ApplicationCommandResult.Rendered();

        string filePath;
        PanelLocation fileLocation;
        try
        {
            fileLocation = ResolvePath(context, target.State, result.FilePath);
            filePath = fileLocation.SourcePath;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            new MessageDialog(context.ModalDialogs).Show("Editor", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        bool existedBefore = target.State.SourceId == PanelSourceId.Local
            ? File.Exists(filePath)
            : context.Controller.CurrentItem(target.State)?.Location == fileLocation;
        EditorDocumentFormat newFileFormat = result.CodePage.CreateDocumentFormat(context.Settings.Editor);
        if (fileLocation.SourceId == PanelSourceId.Local)
        {
            context.EditFileWithNewFileFormat(
                filePath,
                newFileFormat,
                PanelCommandEditorContextFactory.Create(context, target));
        }
        else
        {
            context.EditFile(
                fileLocation,
                PanelCommandEditorContextFactory.Create(context, target));
        }

        if (target.State.SourceId == PanelSourceId.Local && File.Exists(filePath))
            context.History.AddFile(new FileHistoryItem { Path = filePath });

        context.SafeRefresh(target.State, target.VisibleRows);
        if (!existedBefore)
            context.Controller.SetCursorByName(target.State, Path.GetFileName(filePath.Replace('/', Path.DirectorySeparatorChar)), target.VisibleRows);

        return ApplicationCommandResult.Rendered();
    }

    private static string? InitialPath(ApplicationCommandContext context, ResolvedPanelCommandTarget target)
    {
        FilePanelItem? item = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, context.Controller, out var resolvedItem) ? resolvedItem : null;
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

    private static PanelLocation ResolvePath(
        ApplicationCommandContext context,
        FilePanelState state,
        string path)
    {
        if (state.SourceId == PanelSourceId.Local)
            return PanelLocation.Local(ResolveLocalPath(state.SourcePath, path));

        string combined = path.Contains('/') || path.Contains('\\')
            ? path
            : context.CombinePanelPath(state, path);
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
