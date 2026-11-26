using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Ui;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.UserMenu;
using CSharpFar.App.State;
using CSharpFar.App.Rendering;
using CSharpFar.App.Panels;
using CSharpFar.App.Modules;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Dialogs;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class OpenDirectoryShortcutEditorCommand(PanelWorkspaceController workspace, AppSettingsAlias settings, DialogService dialogs, FormFieldFactory fields, Action? saveSettings) : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsOpenDirectoryShortcuts;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        workspace.IsPanelsMode;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context))
            return ApplicationCommandResult.Rendered();

        var normalized = DirectoryShortcutNormalizer.Normalize(settings.DirectoryShortcuts);
        var result = new DirectoryShortcutsDialog(dialogs, fields)
            .Show(normalized, workspace.ActiveState.CurrentDirectory);
        if (!result.Changed)
            return ApplicationCommandResult.Rendered();

        settings.DirectoryShortcuts ??= new();
        settings.DirectoryShortcuts.Items = [.. result.Items];
        saveSettings?.Invoke();
        return ApplicationCommandResult.Rendered();
    }
}
