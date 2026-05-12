using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowStatusLineCommand : SettingToggleCommand
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowStatusLine;

    protected override void Toggle(ApplicationCommandContext context) =>
        context.PanelOptions.ShowStatusLine = !context.PanelOptions.ShowStatusLine;
}
