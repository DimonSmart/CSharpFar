using CSharpFar.App.Dialogs;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class OpenDirectoryShortcutEditorCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsOpenDirectoryShortcuts;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasVisiblePanels;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context))
            return ApplicationCommandResult.Rendered();

        var normalized = DirectoryShortcutNormalizer.Normalize(context.Settings.DirectoryShortcuts);
        var result = new DirectoryShortcutsDialog(context.Screen, context.Palette)
            .Show(normalized, context.ActiveState.CurrentDirectory);
        if (!result.Changed)
            return ApplicationCommandResult.Rendered();

        context.Settings.DirectoryShortcuts ??= new();
        context.Settings.DirectoryShortcuts.Items = [.. result.Items];
        context.SaveSettings();
        return ApplicationCommandResult.Rendered();
    }
}
