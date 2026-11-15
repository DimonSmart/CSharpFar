using CSharpFar.App.Plugins;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class ExternalPluginsDialog(DialogService dialogs, ExternalPluginManager manager)
{
    public void Show()
    {
        dialogs.List(new ListDialogOptions<ExternalPluginEntry, bool>
        {
            Title = manager.NoExtensions ? "Plugins (--no-extensions)" : "Plugins (restart to apply changes)",
            Items = () => manager.Entries,
            ItemText = Format,
            EmptyText = "No external extensions installed.",
            DialogWidth = 88,
            MinDialogWidth = 44,
            MaxVisibleRows = 14,
            DefaultItemActionId = "toggle",
            DetailsActionId = "details",
            Actions =
            [
                DialogButton.Default("toggle", "Enable/Disable", 'E'),
                DialogButton.Action("details", "Details (F4)", 'D'),
                DialogButton.Action("close", "Close", 'C')
            ],
            Cancel = () => false,
            HandleAction = action =>
            {
                if (action.ActionId == "close")
                    return DialogOutcome<bool>.Complete(false);

                if (action.SelectedItem is not { } entry)
                    return DialogOutcome<bool>.ContinueOpen();

                if (action.ActionId == "details")
                {
                    ShowDetails(entry);
                    return DialogOutcome<bool>.ContinueOpen();
                }

                if (action.ActionId != "toggle")
                    return DialogOutcome<bool>.ContinueOpen();

                bool enabled = !entry.Enabled;
                if (!manager.TrySetEnabled(entry, enabled, out string? error))
                {
                    dialogs.Message("Plugins", "Cannot save extension state: " + error);
                    return DialogOutcome<bool>.ContinueOpen();
                }

                dialogs.Message("Plugins",
                    $"'{entry.Name ?? entry.FolderName}' is now {(enabled ? "enabled" : "disabled")} in configuration.\n" +
                    "Changes take effect only after restarting CSharpFar.\n" +
                    (entry.Status == ExternalPluginStatus.Loaded && !enabled
                        ? "The already loaded plugin remains active for this session."
                        : string.Empty));
                return DialogOutcome<bool>.RefreshOpen(action.SelectedIndex);
            }
        });
    }

    private void ShowDetails(ExternalPluginEntry entry)
    {
        string details =
            $"ID: {entry.Id ?? "(manifest unavailable)"}\n" +
            $"Name: {entry.Name ?? "(manifest unavailable)"}\n" +
            $"Version: {entry.Version ?? "(unknown)"}\n" +
            $"Folder: {entry.FolderName}\n" +
            $"Installation: {entry.InstallationPath}\n" +
            $"Current session: {entry.Status}\n" +
            $"Next startup: {(entry.Enabled ? "Enabled" : "Disabled")}\n" +
            (manager.NoExtensions ? "External plugins were skipped by --no-extensions.\n" : "") +
            (entry.FailureStage is null ? "" : $"Failure stage: {entry.FailureStage}\n") +
            (entry.Error is null ? "" : $"Error: {entry.Error}\n") +
            (entry.RequiresRestart ? "Pending change: restart required.\n" : "") +
            "Changes take effect after restart.";
        dialogs.Message("Plugin details", details);
    }

    private static string Format(ExternalPluginEntry entry)
    {
        string name = entry.Name ?? entry.FolderName;
        if (name.Length > 30) name = name[..29] + "…";
        string version = entry.Version ?? "?";
        if (version.Length > 10) version = version[..9] + "…";
        string state = entry.Status.ToString();
        return $"[{(entry.Enabled ? 'X' : ' ')}] {name,-30} {version,-10} {state}" +
               (entry.RequiresRestart ? " (restart)" : "");
    }
}
