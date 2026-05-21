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
        context.ActiveState.SourceId == PanelSourceId.Local &&
        context.HasCapability(context.ActiveState, PanelProviderCapabilities.CreateFile);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context, args))
        {
            context.ShowReadOnlyPanelMessage("Create file");
            return ApplicationCommandResult.Rendered();
        }

        var dialog = new OpenCreateFileDialog(context.Screen);
        var result = dialog.Show(
            InitialPath(context),
            attempt => ValidateLocalPath(context.ActiveState.SourcePath, attempt));

        if (result is null)
            return ApplicationCommandResult.Rendered();

        string filePath;
        try
        {
            filePath = ResolveLocalPath(context.ActiveState.SourcePath, result.FilePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            new MessageDialog(context.Screen, context.Palette).Show("Editor", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        bool existedBefore = File.Exists(filePath);
        EditorDocumentFormat newFileFormat = result.CodePage.CreateDocumentFormat(context.Settings.Editor);
        new FileEditor(
            context.Screen,
            context.Palette,
            context.Settings.Editor,
            context.TextClipboard,
            BuildFileNameInsertionContext(context))
            .ShowWithNewFileFormat(filePath, newFileFormat);

        if (File.Exists(filePath))
            context.History.AddFile(new FileHistoryItem { Path = filePath });

        int visibleRows = context.VisibleRows();
        context.SafeRefresh(context.ActiveState, visibleRows);
        if (!existedBefore && IsInCurrentLocalDirectory(context.ActiveState.SourcePath, filePath))
            context.Controller.SetCursorByName(context.ActiveState, Path.GetFileName(filePath), visibleRows);

        return ApplicationCommandResult.Rendered();
    }

    private static EditorFileNameInsertionContext BuildFileNameInsertionContext(ApplicationCommandContext context)
    {
        var activeItem = context.Controller.CurrentItem(context.ActiveState);
        var passiveItem = context.Controller.CurrentItem(context.PassiveState);
        return new EditorFileNameInsertionContext(
            activeItem is { IsParentDirectory: false } ? activeItem.Name : null,
            activeItem is { IsParentDirectory: false } ? activeItem.FullPath : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.Name : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.FullPath : null);
    }

    private static string? InitialPath(ApplicationCommandContext context)
    {
        var item = context.Controller.CurrentItem(context.ActiveState);
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
