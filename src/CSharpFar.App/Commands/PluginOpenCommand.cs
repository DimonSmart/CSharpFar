using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed record PluginOpenCommandArgs(Guid PluginId, Guid ItemId);

internal sealed class PluginOpenCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.PluginOpen;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        args is PluginOpenCommandArgs;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (args is not PluginOpenCommandArgs pluginArgs)
            return ApplicationCommandResult.Failure("Plugin command arguments are required.");

        return context.OpenPluginMenuItem(pluginArgs.PluginId, pluginArgs.ItemId);
    }
}
