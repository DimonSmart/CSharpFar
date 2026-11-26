namespace CSharpFar.App.Commands;

internal abstract class SettingToggleCommand(PanelSettingsChangeService settingsChange) : IApplicationCommand
{
    public abstract string CommandId { get; }

    public bool CanExecute(object? args = null) => true;

    public ApplicationCommandResult Execute(object? args = null)
    {
        Toggle();
        settingsChange.Apply();
        return ApplicationCommandResult.Rendered();
    }

    protected abstract void Toggle();
}
