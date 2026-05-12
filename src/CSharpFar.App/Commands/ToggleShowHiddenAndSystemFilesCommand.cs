using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowHiddenAndSystemFilesCommand : SettingToggleCommand
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowHiddenAndSystemFiles;

    protected override void Toggle(ApplicationCommandContext context) =>
        context.PanelOptions.ShowHiddenAndSystemFiles = !context.PanelOptions.ShowHiddenAndSystemFiles;
}
