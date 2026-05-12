namespace CSharpFar.App.Commands;

internal abstract class SettingToggleCommand : IApplicationCommand
{
    public abstract string CommandId { get; }

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        Toggle(context);
        context.RefreshPanels();
        context.SaveSettings();
        return ApplicationCommandResult.Rendered();
    }

    protected abstract void Toggle(ApplicationCommandContext context);
}
