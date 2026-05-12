using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleHighlightFilesCommand : SettingToggleCommand
{
    public override string CommandId => MenuCommandIds.SettingsToggleHighlightFiles;

    protected override void Toggle(ApplicationCommandContext context)
    {
        context.Settings.Panels.FileHighlighting.Enabled =
            !context.Settings.Panels.FileHighlighting.Enabled;
        context.HighlightService = context.CreateHighlightService();
    }
}
