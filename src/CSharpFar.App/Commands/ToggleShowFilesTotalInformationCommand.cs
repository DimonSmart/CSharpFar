using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowFilesTotalInformationCommand(AppSettingsAlias settings, PanelSettingsChangeService settingsChange) : SettingToggleCommand(settingsChange)
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowFilesTotalInformation;

    protected override void Toggle() =>
        settings.Panels.Options.ShowFilesTotalInformation = !settings.Panels.Options.ShowFilesTotalInformation;
}
