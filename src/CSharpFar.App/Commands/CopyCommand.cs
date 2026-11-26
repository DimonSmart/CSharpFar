using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class CopyCommand(bool toCurrentDirectory = false) : IApplicationCommand
{
    public string CommandId => toCurrentDirectory
        ? FunctionKeyCommandIds.CopyToCurrentDirectory
        : FunctionKeyCommandIds.Copy;

    public bool CanExecute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        FilePanelState destination = toCurrentDirectory ? target.State : target.PassiveState;
        return context.HasCapability(target.State, PanelProviderCapabilities.CopyFrom) &&
               context.HasCapability(destination, PanelProviderCapabilities.CopyTo);
    }

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        FilePanelState destinationState = toCurrentDirectory ? target.State : target.PassiveState;
        if (!context.HasCapability(target.State, PanelProviderCapabilities.CopyFrom))
        {
            context.ShowReadOnlyPanelMessage("Copy");
            return ApplicationCommandResult.Rendered();
        }

        if (!context.HasCapability(destinationState, PanelProviderCapabilities.CopyTo))
        {
            if (toCurrentDirectory)
                context.ShowReadOnlyPanelMessage("Copy");
            else
                context.Dialogs.Message(
                    "Copy",
                    "Cannot copy to search results panel.\nSearch results are read-only.");
            return ApplicationCommandResult.Rendered();
        }

        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted) ||
            (!toCurrentDirectory &&
             !ApplicationCommandContext.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted)))
            return ApplicationCommandResult.Rendered();

        var sources = FileOperationCommandHelpers.GetOperationSourceLocations(context, target.State, target.ActiveCommitted);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        var (initialDestination, initialUseTemplate) = toCurrentDirectory
            ? BuildSameDirectoryDestination(context, target.State, sources)
            : (target.PassiveCommitted?.CurrentDirectory ?? target.PassiveState.CurrentDirectory, false);

        var initialOptions = context.BuildFileOperationOptions();
        if (toCurrentDirectory)
            initialOptions = initialOptions with { DefaultConflictDecision = ConflictDecisionMode.Rename };

        FileOperationRequest CreateRequest(FileOperationDialogResult result) => new()
        {
            Kind = FileOperationKind.Copy,
            Sources = sources,
            UseDestinationTemplate = result.UseDestinationTemplate,
            Destination = new PanelLocation(destinationState.SourceId, result.Destination),
            Options = result.Options,
        };

        var dialogResult = new FileOperationDialog(context.Dialogs, context.Fields, context.ShowHelp).ShowCopy(
            sources.Select(location => location.SourcePath).ToArray(),
            initialDestination,
            initialOptions,
            result => DestinationTemplatePreview.Show(context, CreateRequest(result)),
            initialUseTemplate: initialUseTemplate);
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        try
        {
            context.ExecuteFileOperation(CreateRequest(dialogResult));

            target.State.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            context.Dialogs.Message("Copy Error", ex.Message);
        }

        context.RefreshPanelsAfterFileOperation();
        return ApplicationCommandResult.Rendered();
    }

    private static (string Destination, bool UseTemplate) BuildSameDirectoryDestination(
        ApplicationCommandContext context,
        FilePanelState state,
        IReadOnlyList<PanelLocation> sources)
    {
        if (sources.Count == 1)
        {
            string name = context.TryGetSource(sources[0].SourceId, out var source)
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
                return (context.CombinePanelPath(state, copyName), false);
            }
        }

        const string nameTemplate = "{name} (copy){ext}";
        string fullPath = context.CombinePanelPath(state, nameTemplate);
        string literalDirectoryPrefix = fullPath[..^nameTemplate.Length]
            .Replace("{", "{{", StringComparison.Ordinal)
            .Replace("}", "}}", StringComparison.Ordinal);
        return (literalDirectoryPrefix + nameTemplate, true);
    }
}
