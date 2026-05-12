using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowFilesTotalInformationCommand : SettingToggleCommand
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowFilesTotalInformation;

    protected override void Toggle(ApplicationCommandContext context) =>
        context.PanelOptions.ShowFilesTotalInformation = !context.PanelOptions.ShowFilesTotalInformation;
}
