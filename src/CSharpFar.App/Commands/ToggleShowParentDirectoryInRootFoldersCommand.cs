using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowParentDirectoryInRootFoldersCommand(AppSettingsAlias settings, PanelSettingsChangeService settingsChange) : SettingToggleCommand(settingsChange)
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowParentDirectoryInRootFolders;

    protected override void Toggle() =>
        settings.Panels.Options.ShowParentDirectoryInRootFolders =
            !settings.Panels.Options.ShowParentDirectoryInRootFolders;
}
