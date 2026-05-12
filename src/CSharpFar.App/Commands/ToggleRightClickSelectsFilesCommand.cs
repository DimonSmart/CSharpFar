using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleRightClickSelectsFilesCommand : SettingToggleCommand
{
    public override string CommandId => MenuCommandIds.SettingsToggleRightClickSelectsFiles;

    protected override void Toggle(ApplicationCommandContext context) =>
        context.PanelOptions.RightClickSelectsFiles = !context.PanelOptions.RightClickSelectsFiles;
}
