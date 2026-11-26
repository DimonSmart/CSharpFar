using CSharpFar.App.Dialogs;
using CSharpFar.App.Plugins;
using CSharpFar.Core.Menu;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class OpenPluginsCommand(DialogService dialogs, ExternalPluginSession plugins) : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsOpenPlugins;
    public bool CanExecute(object? args = null) => true;

    public ApplicationCommandResult Execute(object? args = null)
    {
        if (plugins.Manager is { } manager)
            new ExternalPluginsDialog(dialogs, manager).Show();
        else
            dialogs.Message("Plugins", "External extension management is unavailable in this session.");
        return ApplicationCommandResult.Rendered();
    }
}
