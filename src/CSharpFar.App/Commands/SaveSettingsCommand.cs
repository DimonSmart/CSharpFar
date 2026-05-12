using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class SaveSettingsCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsSave;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.CanSaveSettings;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!context.CanSaveSettings)
            return ApplicationCommandResult.Failure("Settings save callback is not available.");

        context.SaveSettings();
        return ApplicationCommandResult.Rendered();
    }
}
