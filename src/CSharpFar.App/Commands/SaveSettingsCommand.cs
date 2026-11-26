using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class SaveSettingsCommand(Action? saveSettings) : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsSave;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        saveSettings is not null;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (saveSettings is null)
            return ApplicationCommandResult.Failure("Settings save callback is not available.");

        saveSettings.Invoke();
        return ApplicationCommandResult.Rendered();
    }
}
