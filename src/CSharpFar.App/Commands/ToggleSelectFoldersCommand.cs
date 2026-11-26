using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleSelectFoldersCommand(AppSettingsAlias settings, PanelSettingsChangeService settingsChange) : SettingToggleCommand(settingsChange)
{
    public override string CommandId => MenuCommandIds.SettingsToggleSelectFolders;

    protected override void Toggle() =>
        settings.Panels.Options.SelectFolders = !settings.Panels.Options.SelectFolders;
}
