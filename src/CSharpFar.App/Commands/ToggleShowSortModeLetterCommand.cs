using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowSortModeLetterCommand : SettingToggleCommand
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowSortModeLetter;

    protected override void Toggle(ApplicationCommandContext context) =>
        context.PanelOptions.ShowSortModeLetter = !context.PanelOptions.ShowSortModeLetter;
}
