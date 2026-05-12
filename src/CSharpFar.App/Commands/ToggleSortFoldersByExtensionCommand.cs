using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class ToggleSortFoldersByExtensionCommand : SettingToggleCommand
{
    public override string CommandId => MenuCommandIds.SettingsToggleSortFoldersByExtension;

    protected override void Toggle(ApplicationCommandContext context) =>
        context.PanelOptions.SortFoldersByExtension = !context.PanelOptions.SortFoldersByExtension;
}
