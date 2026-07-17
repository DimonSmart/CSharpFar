using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed record NavigateToRootArgs(
    PanelSide Side,
    string CommittedCurrentDirectory);

internal sealed class NavigateToRootCommand : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.NavigateToRoot;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        PanelSide side = args is NavigateToRootArgs rootArgs
            ? rootArgs.Side
            : context.ActiveSide;
        FilePanelState state = context.GetPanelState(side);
        string currentDirectory = args is NavigateToRootArgs committedArgs
            ? committedArgs.CommittedCurrentDirectory
            : state.CurrentDirectory;

        if (!string.Equals(state.CurrentDirectory, currentDirectory, StringComparison.OrdinalIgnoreCase))
            return ApplicationCommandResult.Rendered();

        var root = Path.GetPathRoot(currentDirectory);
        if (string.IsNullOrEmpty(root))
            return ApplicationCommandResult.Rendered();

        if (string.Equals(currentDirectory, root, StringComparison.OrdinalIgnoreCase))
            return ApplicationCommandResult.Rendered();

        bool loaded = context.Controller.TryLoadLocation(
            state,
            PanelLocation.Local(root),
            context.PanelOptions);

        if (loaded)
            context.StartWatching(state, side);

        return ApplicationCommandResult.Rendered();
    }
}
