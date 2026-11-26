using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Services;
using CSharpFar.App.Viewer;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class CreateFolderCommand(PanelCommandTargetResolver targetResolver, PanelController controller, DialogService dialogs, FilePanelSourceRegistry sourceRegistry, PanelFileOperationWorkflow operations, PanelRefreshService panelRefresh) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.CreateFolder;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        FileOperationCommandHelpers.HasCapability(targetResolver.ResolvePanelTarget(args).State, PanelProviderCapabilities.CreateDirectory);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
        {
            FileOperationCommandHelpers.ShowReadOnlyPanelMessage(dialogs, "Create folder");
            return ApplicationCommandResult.Rendered();
        }

        if (!CommittedLocationMatches(target))
            return ApplicationCommandResult.Rendered();

        string? name = dialogs.Input(new SingleLineInputDialogOptions
        {
            Title = "Make folder",
            Prompt = "Create the folder:",
            History = AppTextHistoryIds.CreateFolderName,
            PreferredWidth = 64,
            PreferredHeight = 11,
            ErrorLines = (attempt, error) =>
                error.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                    ? [attempt, "already exists."]
                    : [error],
            Validate = attempt =>
            {
                if (target.State.SourceId == PanelSourceId.Local && attempt.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    return "Invalid characters in folder path.";

                string newPath = FileOperationCommandHelpers.CombinePanelPath(sourceRegistry, target.State, attempt);
                try
                {
                    operations.Execute(new FileOperationRequest
                    {
                        Kind = FileOperationKind.CreateDirectory,
                        Sources = [],
                        Destination = new PanelLocation(target.State.SourceId, newPath),
                        Options = operations.BuildOptions(),
                    });
                    return null;
                }
                catch (IOException ex) { return ex.Message; }
                catch (UnauthorizedAccessException) { return "Access denied."; }
                catch (ArgumentException ex) { return ex.Message; }
            },
        });

        if (name is null)
            return ApplicationCommandResult.Rendered();

        panelRefresh.SafeRefresh(target.State, target.VisibleRows);
        controller.SetCursorByName(target.State, FirstCreatedDirectoryName(name, sourceRegistry.TryGetSource(target.State.SourceId, out var source) ? source.PathSeparators.ToArray() : []), target.VisibleRows);
        return ApplicationCommandResult.Rendered();
    }

    private static bool CommittedLocationMatches(ResolvedPanelCommandTarget target) =>
        PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted);

    private static string FirstCreatedDirectoryName(string path, char[] separators)
    {
        string trimmed = path.Trim(separators);
        int separator = trimmed.IndexOfAny(separators);
        return separator < 0 ? trimmed : trimmed[..separator];
    }
}
