using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleSelectFoldersCommand : SettingToggleCommand
{
    public override string CommandId => MenuCommandIds.SettingsToggleSelectFolders;

    protected override void Toggle(ApplicationCommandContext context) =>
        context.PanelOptions.SelectFolders = !context.PanelOptions.SelectFolders;
}
