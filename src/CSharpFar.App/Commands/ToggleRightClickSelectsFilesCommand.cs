using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleRightClickSelectsFilesCommand(AppSettingsAlias settings, PanelSettingsChangeService settingsChange) : SettingToggleCommand(settingsChange)
{
    public override string CommandId => MenuCommandIds.SettingsToggleRightClickSelectsFiles;

    protected override void Toggle() =>
        settings.Panels.Options.RightClickSelectsFiles = !settings.Panels.Options.RightClickSelectsFiles;
}
