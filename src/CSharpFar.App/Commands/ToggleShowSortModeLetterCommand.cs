using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowSortModeLetterCommand(AppSettingsAlias settings, PanelSettingsChangeService settingsChange) : SettingToggleCommand(settingsChange)
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowSortModeLetter;

    protected override void Toggle() =>
        settings.Panels.Options.ShowSortModeLetter = !settings.Panels.Options.ShowSortModeLetter;
}
