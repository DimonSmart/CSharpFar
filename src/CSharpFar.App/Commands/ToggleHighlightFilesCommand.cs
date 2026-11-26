using CSharpFar.App.Highlighting;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleHighlightFilesCommand(AppSettingsAlias settings, PanelSettingsChangeService settingsChange, FileHighlightState highlightState) : SettingToggleCommand(settingsChange)
{
    public override string CommandId => MenuCommandIds.SettingsToggleHighlightFiles;

    protected override void Toggle()
    {
        settings.Panels.FileHighlighting.Enabled =
            !settings.Panels.FileHighlighting.Enabled;
        highlightState.Current = FileHighlightServiceFactory.Create(settings);
    }
}
