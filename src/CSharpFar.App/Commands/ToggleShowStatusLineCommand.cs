using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowStatusLineCommand(AppSettingsAlias settings, PanelSettingsChangeService settingsChange) : SettingToggleCommand(settingsChange)
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowStatusLine;

    protected override void Toggle() =>
        settings.Panels.Options.ShowStatusLine = !settings.Panels.Options.ShowStatusLine;
}
