using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleSortFoldersByExtensionCommand(AppSettingsAlias settings, PanelSettingsChangeService settingsChange) : SettingToggleCommand(settingsChange)
{
    public override string CommandId => MenuCommandIds.SettingsToggleSortFoldersByExtension;

    protected override void Toggle() =>
        settings.Panels.Options.SortFoldersByExtension = !settings.Panels.Options.SortFoldersByExtension;
}
