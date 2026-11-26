using CSharpFar.Ui;
using CSharpFar.FileSystem;
using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Services;
using CSharpFar.App.Viewer;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class CopyCommand(PanelCommandTargetResolver targetResolver, PanelController controller, DialogService dialogs, FormFieldFactory fields, FilePanelSourceRegistry sourceRegistry, PanelFileOperationWorkflow operations, Action<HelpTopic> showHelp, bool toCurrentDirectory = false) : IApplicationCommand
{
    public string CommandId => toCurrentDirectory
        ? FunctionKeyCommandIds.CopyToCurrentDirectory
        : FunctionKeyCommandIds.Copy;

    public bool CanExecute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        FilePanelState destination = toCurrentDirectory ? target.State : target.PassiveState;
        return FileOperationCommandHelpers.HasCapability(target.State, PanelProviderCapabilities.CopyFrom) &&
               FileOperationCommandHelpers.HasCapability(destination, PanelProviderCapabilities.CopyTo);
    }

    public ApplicationCommandResult Execute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        FilePanelState destinationState = toCurrentDirectory ? target.State : target.PassiveState;
        if (!FileOperationCommandHelpers.HasCapability(target.State, PanelProviderCapabilities.CopyFrom))
        {
            FileOperationCommandHelpers.ShowReadOnlyPanelMessage(dialogs, "Copy");
            return ApplicationCommandResult.Rendered();
        }

        if (!FileOperationCommandHelpers.HasCapability(destinationState, PanelProviderCapabilities.CopyTo))
        {
            if (toCurrentDirectory)
                FileOperationCommandHelpers.ShowReadOnlyPanelMessage(dialogs, "Copy");
            else
                dialogs.Message(
                    "Copy",
                    "Cannot copy to search results panel.\nSearch results are read-only.");
            return ApplicationCommandResult.Rendered();
        }

        if (!PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted) ||
            (!toCurrentDirectory &&
             !PanelCommandTargetResolver.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted)))
            return ApplicationCommandResult.Rendered();

        var sources = FileOperationCommandHelpers.GetOperationSourceLocations(controller, target.State, target.ActiveCommitted);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        var (initialDestination, initialUseTemplate) = toCurrentDirectory
            ? BuildSameDirectoryDestination(target.State, sources)
            : (target.PassiveCommitted?.CurrentDirectory ?? target.PassiveState.CurrentDirectory, false);

        var initialOptions = operations.BuildOptions();
        if (toCurrentDirectory)
            initialOptions = initialOptions with { DefaultConflictDecision = ConflictDecisionMode.Rename };

        FileOperationRequest CreateRequest(FileOperationDialogResult result) => new()
        {
            Kind = FileOperationKind.Copy,
            Sources = sources,
            UseDestinationTemplate = result.UseDestinationTemplate,
            Destination = new PanelLocation(destinationState.SourceId,
                FileOperationCommandHelpers.BindLocalDestination(destinationState, sources, result)),
            Options = result.Options,
        };

        var dialogResult = new FileOperationDialog(dialogs, fields, showHelp).ShowCopy(
            sources.Select(location => location.SourcePath).ToArray(),
            initialDestination,
            initialOptions,
            result => operations.Preview(CreateRequest(result)),
            initialUseTemplate: initialUseTemplate,
            unsafeReplacement: sourceRegistry.TryGetSource(destinationState.SourceId, out var writeTarget) &&
                SafeFileWriteService.UsesBestEffort(writeTarget, overwrite: true));
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        try
        {
            operations.Execute(CreateRequest(dialogResult));

            target.State.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            dialogs.Message("Copy Error", ex.Message);
        }

        operations.RefreshAfterOperation();
        return ApplicationCommandResult.Rendered();
    }

    private (string Destination, bool UseTemplate) BuildSameDirectoryDestination(
        FilePanelState state,
        IReadOnlyList<PanelLocation> sources)
    {
        if (sources.Count == 1)
        {
            string name = sourceRegistry.TryGetSource(sources[0].SourceId, out var source)
                ? source.GetFileName(sources[0].SourcePath)
                : throw new InvalidOperationException("Source is unavailable.");
            bool isDirectory = state.Items.Any(item =>
                state.SelectedLocations.Comparer.Equals(item.Location, sources[0]) && item.IsDirectory);
            if (!isDirectory)
            {
                int dot = name.LastIndexOf('.');
                string copyName = dot > 0 && dot < name.Length - 1
                    ? name[..dot] + " (copy)" + name[dot..]
                    : name + " (copy)";
                return (FileOperationCommandHelpers.CombinePanelPath(sourceRegistry, state, copyName), false);
            }
        }

        const string nameTemplate = "{name} (copy){ext}";
        string fullPath = FileOperationCommandHelpers.CombinePanelPath(sourceRegistry, state, nameTemplate);
        string literalDirectoryPrefix = fullPath[..^nameTemplate.Length]
            .Replace("{", "{{", StringComparison.Ordinal)
            .Replace("}", "}}", StringComparison.Ordinal);
        return (literalDirectoryPrefix + nameTemplate, true);
    }
}
