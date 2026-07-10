using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class CreateFolderCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.CreateFolder;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ActiveState, PanelProviderCapabilities.CreateDirectory);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context, args))
        {
            context.ShowReadOnlyPanelMessage("Create folder");
            return ApplicationCommandResult.Rendered();
        }

        var dialog = new CreateFolderDialog(context.ModalDialogs);
        string? name = dialog.Show(validate: attempt =>
        {
            if (attempt.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return "Invalid characters in folder path.";

            string newPath = context.CombinePanelPath(context.ActiveState, attempt);
            try
            {
                context.ExecuteFileOperation(new FileOperationRequest
                {
                    Kind = FileOperationKind.CreateDirectory,
                    Sources = [],
                    Destination = newPath,
                    DestinationLocation = new PanelLocation(context.ActiveState.SourceId, newPath),
                    Options = context.BuildFileOperationOptions(),
                });
                return null;
            }
            catch (IOException ex) { return ex.Message; }
            catch (UnauthorizedAccessException) { return "Access denied."; }
            catch (ArgumentException ex) { return ex.Message; }
        });

        if (name is null)
            return ApplicationCommandResult.Rendered();

        int visibleRows = context.VisibleRows();
        context.SafeRefresh(context.ActiveState, visibleRows);
        context.Controller.SetCursorByName(context.ActiveState, FirstCreatedDirectoryName(name), visibleRows);
        return ApplicationCommandResult.Rendered();
    }

    private static string FirstCreatedDirectoryName(string path)
    {
        string trimmed = path.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int separator = trimmed.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return separator < 0 ? trimmed : trimmed[..separator];
    }
}
