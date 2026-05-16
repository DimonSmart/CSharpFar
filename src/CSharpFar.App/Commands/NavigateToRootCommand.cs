using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class NavigateToRootCommand : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.NavigateToRoot;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var state = context.ActiveState;
        var root = Path.GetPathRoot(state.CurrentDirectory);
        if (string.IsNullOrEmpty(root))
            return ApplicationCommandResult.Rendered();

        if (string.Equals(state.CurrentDirectory, root, StringComparison.OrdinalIgnoreCase))
            return ApplicationCommandResult.Rendered();

        bool loaded = context.Controller.TryLoadLocation(
            state,
            PanelLocation.Local(root),
            context.PanelOptions);

        if (loaded)
            context.StartWatching(state, context.ActiveSide);

        return ApplicationCommandResult.Rendered();
    }
}
