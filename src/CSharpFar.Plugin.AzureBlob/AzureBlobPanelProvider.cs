using Azure;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Plugin.AzureBlob;

public sealed class AzureBlobPanelProvider : IPanelProvider
{
    public string Id => "azure-blob";
    public string Title => "Azure Blob Storage";

    public ValueTask<PluginPanelOpenRequest> OpenAsync(IPanelProviderContext context, CancellationToken cancellationToken)
    {
        PluginPanelOpenRequest? request;
        try { request = Open(context.Ui, context.Settings, cancellationToken); }
        catch (Exception error) { throw AzureErrors.Safe(error, cancellationToken); }
        return ValueTask.FromResult(request ?? throw new OperationCanceledException("Azure connection cancelled."));
    }

    public ValueTask<PluginCommandResult> ExecuteAsync(IPluginUiService ui, IPluginSettingsService settings, CancellationToken token)
    {
        try
        {
            var request = Open(ui, settings, token);
            return ValueTask.FromResult(request is null ? PluginCommandResult.Cancelled() : PluginCommandResult.OpenPanel(request));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return ValueTask.FromResult(PluginCommandResult.Cancelled()); }
        catch (Exception error) { return ValueTask.FromResult(PluginCommandResult.Failed(AzureErrors.Message(error))); }
    }

    private static PluginPanelOpenRequest? Open(IPluginUiService ui, IPluginSettingsService settings, CancellationToken token)
    {
        if (!ui.SupportsForms)
            throw new AzureOperationException("Azure Blob Storage requires a host with plugin SDK 2.1 forms.");

        var store = new AzureBlobConnectionStore(settings.GetSettingsDirectory("azure-blob"));
        var connections = store.Load();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var entries = connections.Select(c => c.DisplayName).Concat(["Quick connect...", "New connection..."]).ToArray();
            int? index = ui.ShowMenu("Azure Blob Storage", entries, 0);
            if (index is null) return null;
            if (index >= connections.Count)
            {
                var draft = AzureBlobConnectionDialog.Show(ui, null, index == connections.Count + 1, token);
                if (draft is null) continue;
                if (draft.Save)
                {
                    connections.Add(draft.Connection);
                    store.Save(connections);
                }
                if (draft.Panel is not null) return draft.Panel;
                continue;
            }

            var selected = connections[index.Value];
            switch (ui.ShowMenu(selected.DisplayName, ["Connect", "Edit connection...", "Delete connection..."], 0))
            {
                case 0:
                case 1:
                    var edited = AzureBlobConnectionDialog.Show(ui, selected, true, token);
                    if (edited is null) break;
                    if (edited.Save)
                    {
                        connections[index.Value] = edited.Connection;
                        store.Save(connections);
                    }
                    if (edited.Panel is not null) return edited.Panel;
                    break;
                case 2:
                    if (ui.Confirm("Delete connection", "Remove saved settings?", selected.DisplayName))
                    {
                        connections.RemoveAt(index.Value);
                        store.Save(connections);
                    }
                    break;
            }
        }
    }
}

internal static class AzureErrors
{
    // Never propagate raw Azure exception messages or inner exceptions, which may contain a SAS URI.
    public static string Message(Exception error) => error switch
    {
        RequestFailedException failure => failure.Status switch
        {
            0 => "Azure storage is unreachable or the network operation failed.",
            401 => "Azure authentication failed (HTTP 401). Check credentials.",
            403 => "Azure access denied (HTTP 403). Check permissions or SAS expiration.",
            404 => "Azure container or object was not found (HTTP 404).",
            409 => "Azure object conflict (HTTP 409). Refresh before retrying.",
            412 => "Azure object changed concurrently or already exists (HTTP 412).",
            429 => "Azure storage is throttling requests (HTTP 429).",
            _ => $"Azure request failed (HTTP {failure.Status}). Check permissions, connection and object state."
        },
        Azure.Identity.AuthenticationFailedException => "Azure CLI authentication failed. Run az login and check the account's storage access.",
        OperationCanceledException => "Azure network operation timed out or was interrupted.",
        AzureOperationException => error.Message,
        _ => "Azure operation failed. Check connection settings, path and permissions."
    };

    public static Exception Safe(Exception error, CancellationToken token = default) =>
        error is OperationCanceledException && token.IsCancellationRequested
            ? new OperationCanceledException("Azure operation cancelled.", token)
            : new AzureOperationException(Message(error));
}

internal sealed class AzureOperationException(string message) : IOException(message);
