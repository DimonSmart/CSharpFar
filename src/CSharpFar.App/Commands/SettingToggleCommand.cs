namespace CSharpFar.App.Commands;

internal abstract class SettingToggleCommand(PanelSettingsChangeService settingsChange) : IApplicationCommand
{
    public abstract string CommandId { get; }

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        Toggle();
        settingsChange.Apply();
        return ApplicationCommandResult.Rendered();
    }

    protected abstract void Toggle();
}
