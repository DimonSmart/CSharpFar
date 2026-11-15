using CSharpFar.App.Dialogs;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class OpenPluginsCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsOpenPlugins;
    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (context.ExternalPlugins is { } manager)
            new ExternalPluginsDialog(context.Dialogs, manager).Show();
        else
            context.ShowMessage("Plugins", "External extension management is unavailable in this session.");
        return ApplicationCommandResult.Rendered();
    }
}
