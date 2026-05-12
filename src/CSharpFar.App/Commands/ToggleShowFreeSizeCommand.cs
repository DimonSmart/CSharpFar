using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowFreeSizeCommand : SettingToggleCommand
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowFreeSize;

    protected override void Toggle(ApplicationCommandContext context) =>
        context.PanelOptions.ShowFreeSize = !context.PanelOptions.ShowFreeSize;
}
