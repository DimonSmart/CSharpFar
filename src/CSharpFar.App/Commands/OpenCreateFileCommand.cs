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
        context.ResolvePanelTarget(args).State.SourceId == PanelSourceId.Local &&
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.CreateFile);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
        {
            context.ShowReadOnlyPanelMessage("Create file");
            return ApplicationCommandResult.Rendered();
        }

        if (!ApplicationCommandContext.CommittedDirectoryMatches(target.State, target.ActiveCommitted))
            return ApplicationCommandResult.Rendered();

        var dialog = new OpenCreateFileDialog(context.ModalDialogs);
        var result = dialog.Show(
            InitialPath(context, target),
            attempt => ValidateLocalPath(target.State.SourcePath, attempt));

        if (result is null)
            return ApplicationCommandResult.Rendered();

        string filePath;
        try
        {
            filePath = ResolveLocalPath(target.State.SourcePath, result.FilePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            new MessageDialog(context.ModalDialogs).Show("Editor", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        bool existedBefore = File.Exists(filePath);
        EditorDocumentFormat newFileFormat = result.CodePage.CreateDocumentFormat(context.Settings.Editor);
        new FileEditor(
            context.Screen,
            context.ModalDialogs,
            context.Palette,
            context.Settings.Editor,
            context.TextClipboard,
            BuildFileNameInsertionContext(context, target))
            .ShowWithNewFileFormat(filePath, newFileFormat);

        if (File.Exists(filePath))
            context.History.AddFile(new FileHistoryItem { Path = filePath });

        context.SafeRefresh(target.State, target.VisibleRows);
        if (!existedBefore && IsInCurrentLocalDirectory(target.State.SourcePath, filePath))
            context.Controller.SetCursorByName(target.State, Path.GetFileName(filePath), target.VisibleRows);

        return ApplicationCommandResult.Rendered();
    }

    private static EditorFileNameInsertionContext BuildFileNameInsertionContext(
        ApplicationCommandContext context,
        ResolvedPanelCommandTarget target)
    {
        FilePanelItem? activeItem = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, context.Controller, out var resolvedActive) ? resolvedActive : null;
        FilePanelItem? passiveItem = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.PassiveState, target.PassiveCommitted, context.Controller, out var resolvedPassive) ? resolvedPassive : null;
        return new EditorFileNameInsertionContext(
            activeItem is { IsParentDirectory: false } ? activeItem.Name : null,
            activeItem is { IsParentDirectory: false } ? activeItem.FullPath : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.Name : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.FullPath : null);
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
