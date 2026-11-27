using CSharpFar.App.Plugins;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class ExternalPluginsDialog(DialogService dialogs, ExternalPluginManager manager)
{
    public void Show()
    {
        // Keep a global row even when no extensions are installed; real entries stay first.
        var global = new ExternalPluginEntry(string.Empty, string.Empty, true);
        dialogs.List(new ListDialogOptions<ExternalPluginEntry, bool>
        {
            Title = manager.NoExtensions ? "Plugins (--no-extensions)" : "Plugins (restart to apply changes)",
            Items = () => manager.Entries.Append(global).ToArray(),
            ItemText = entry => ReferenceEquals(entry, global)
                ? $"[{(manager.ExternalPluginsEnabled ? 'X' : ' ')}] Enable external plugins (all)" +
                  (manager.GlobalRestartRequired ? " (restart)" : "")
                : Format(entry),
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
                DialogButton.Action("global", "All on/off", 'A'),
                DialogButton.Action("close", "Close", 'C')
            ],
            Cancel = () => false,
            HandleAction = action =>
            {
                if (action.ActionId == "close")
                    return DialogOutcome<bool>.Complete(false);

                if (action.ActionId == "global" ||
                    (action.ActionId == "toggle" && ReferenceEquals(action.SelectedItem, global)))
                {
                    bool enableAll = !manager.ExternalPluginsEnabled;
                    if (!manager.TrySetExternalPluginsEnabled(enableAll, out string? globalError))
                        dialogs.Message("Plugins", "Cannot save extension state: " + globalError);
                    else
                        dialogs.Message("Plugins",
                            $"External plugins are now {(enableAll ? "enabled" : "disabled")} in configuration.\n" +
                            "Individual choices are preserved. Changes take effect after restarting CSharpFar.");
                    return DialogOutcome<bool>.RefreshOpen(action.SelectedIndex);
                }

                if (action.SelectedItem is not { } entry || ReferenceEquals(entry, global))
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
            $"API: {entry.ApiVersion?.ToString() ?? "(unknown)"}\n" +
            $"Folder: {entry.FolderName}\n" +
            $"Installation: {entry.InstallationPath}\n" +
            $"Current session: {entry.Status}\n" +
            $"Individual setting: {(entry.Enabled ? "Enabled" : "Disabled")}\n" +
            $"External plugins: {(manager.ExternalPluginsEnabled ? "Enabled" : "Disabled")}\n" +
            $"Next startup: {(manager.WillLoad(entry) ? "Enabled" : "Disabled")}\n" +
            (manager.NoExtensions ? "External plugins were skipped by --no-extensions.\n" : "") +
            (entry.FailureStage is null ? "" : $"Failure stage: {entry.FailureStage}\n") +
            (entry.Error is null ? "" : $"Error: {entry.Error}\n") +
            (entry.RequiresRestart || manager.GlobalRestartRequired ? "Pending change: restart required.\n" : "") +
            "Changes take effect after restart.";
        dialogs.Message("Plugin details", details);
    }

    private string Format(ExternalPluginEntry entry)
    {
        string name = entry.Name ?? entry.FolderName;
        if (name.Length > 30) name = name[..29] + "…";
        string version = entry.Version ?? "?";
        if (version.Length > 10) version = version[..9] + "…";
        string state = entry.Status.ToString();
        return $"[{(entry.Enabled ? 'X' : ' ')}] {name,-30} {version,-10} {state}" +
               (!manager.ExternalPluginsEnabled ? " (all off)" : "") +
               (entry.RequiresRestart || manager.GlobalRestartRequired ? " (restart)" : "");
    }
}
