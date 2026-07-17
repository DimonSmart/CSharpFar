using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class CreateFolderCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.CreateFolder;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.CreateDirectory);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
        {
            context.ShowReadOnlyPanelMessage("Create folder");
            return ApplicationCommandResult.Rendered();
        }

        if (!CommittedDirectoryMatches(target))
            return ApplicationCommandResult.Rendered();

        var dialog = new CreateFolderDialog(context.ModalDialogs);
        string? name = dialog.Show(validate: attempt =>
        {
            if (attempt.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return "Invalid characters in folder path.";

            string newPath = context.CombinePanelPath(target.State, attempt);
            try
            {
                context.ExecuteFileOperation(new FileOperationRequest
                {
                    Kind = FileOperationKind.CreateDirectory,
                    Sources = [],
                    Destination = newPath,
                    DestinationLocation = new PanelLocation(target.State.SourceId, newPath),
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

        context.SafeRefresh(target.State, target.VisibleRows);
        context.Controller.SetCursorByName(target.State, FirstCreatedDirectoryName(name), target.VisibleRows);
        return ApplicationCommandResult.Rendered();
    }

    private static bool CommittedDirectoryMatches(ResolvedPanelCommandTarget target) =>
        ApplicationCommandContext.CommittedDirectoryMatches(target.State, target.ActiveCommitted);

    private static string FirstCreatedDirectoryName(string path)
    {
        string trimmed = path.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int separator = trimmed.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return separator < 0 ? trimmed : trimmed[..separator];
    }
}
