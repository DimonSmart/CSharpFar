using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Panels;
using CSharpFar.Core.Services;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed record NavigateToRootArgs(
    PanelSide Side,
    string CommittedCurrentDirectory);

internal sealed class NavigateToRootCommand(PanelWorkspaceController workspace, PanelNavigationService navigation,
    PanelAutoRefreshService autoRefresh, FilePanelSourceRegistry sourceRegistry) : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.NavigateToRoot;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        PanelSide side = args is NavigateToRootArgs rootArgs
            ? rootArgs.Side
            : workspace.ActiveSide;
        FilePanelState state = workspace.GetPanelState(side);
        string currentDirectory = args is NavigateToRootArgs committedArgs
            ? committedArgs.CommittedCurrentDirectory
            : state.CurrentDirectory;

        if (!string.Equals(state.CurrentDirectory, currentDirectory, StringComparison.OrdinalIgnoreCase))
            return ApplicationCommandResult.Rendered();

        string? root = TryResolveRootPath(state, currentDirectory);
        if (string.IsNullOrEmpty(root))
            return ApplicationCommandResult.Rendered();

        if (string.Equals(currentDirectory, root, StringComparison.OrdinalIgnoreCase))
            return ApplicationCommandResult.Rendered();

        navigation.Navigate(state, new PanelLocation(state.SourceId, root), preserveOnError: false,
            loaded => { if (loaded) autoRefresh.StartWatching(state, workspace.PanelSideForState(state)); });

        return ApplicationCommandResult.Rendered();
    }

    private string? TryResolveRootPath(
        FilePanelState state,
        string currentDirectory)
    {
        if (state.SourceId == PanelSourceId.Local)
            return Path.GetPathRoot(currentDirectory);

        if (!sourceRegistry.TryGetSource(state.SourceId, out var source))
            return null;

        string root = source.NormalizePath(currentDirectory);
        while (!source.IsRootPath(root))
        {
            string? parent = source.GetParentPath(root);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, root, StringComparison.Ordinal))
                break;

            root = parent;
        }

        return root;
    }
}
