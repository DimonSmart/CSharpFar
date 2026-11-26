using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowHiddenAndSystemFilesCommand(AppSettingsAlias settings, PanelSettingsChangeService settingsChange) : SettingToggleCommand(settingsChange)
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowHiddenAndSystemFiles;

    protected override void Toggle() =>
        settings.Panels.Options.ShowHiddenAndSystemFiles = !settings.Panels.Options.ShowHiddenAndSystemFiles;
}
