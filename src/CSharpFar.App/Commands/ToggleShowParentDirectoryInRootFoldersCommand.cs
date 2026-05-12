using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleShowParentDirectoryInRootFoldersCommand : SettingToggleCommand
{
    public override string CommandId => MenuCommandIds.SettingsToggleShowParentDirectoryInRootFolders;

    protected override void Toggle(ApplicationCommandContext context) =>
        context.PanelOptions.ShowParentDirectoryInRootFolders =
            !context.PanelOptions.ShowParentDirectoryInRootFolders;
}
