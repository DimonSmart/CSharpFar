using Azure;
using Azure.Storage.Blobs;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Plugin.AzureBlob;

public sealed class AzureBlobPanelProvider : IPanelProvider
{
    public string Id => "azure-blob";
    public string Title => "Azure Blob Storage";

    public ValueTask<PluginPanelOpenRequest> OpenAsync(IPanelProviderContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(Open(context.Ui, context.Settings, cancellationToken)
            ?? throw new OperationCanceledException("Azure connection cancelled."));

    public ValueTask<PluginCommandResult> ExecuteAsync(IPluginUiService ui, IPluginSettingsService settings, CancellationToken token)
    {
        try
        {
            var request = Open(ui, settings, token);
            return ValueTask.FromResult(request is null ? PluginCommandResult.Cancelled() : PluginCommandResult.OpenPanel(request));
        }
        catch (OperationCanceledException) { return ValueTask.FromResult(PluginCommandResult.Cancelled()); }
        catch (Exception error) { return ValueTask.FromResult(PluginCommandResult.Failed(AzureErrors.Message(error))); }
    }

    private static PluginPanelOpenRequest? Open(IPluginUiService ui, IPluginSettingsService settings, CancellationToken token)
    {
        var store = new AzureBlobConnectionStore(settings.GetSettingsDirectory("azure-blob"));
        var connections = store.Load();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var entries = connections.Select(c => c.DisplayName).Concat(["Quick connect...", "New connection..."]).ToArray();
            int? index = ui.ShowMenu("Azure Blob Storage", entries, 0);
            if (index is null) return null;
            if (index == connections.Count || index == connections.Count + 1)
            {
                var connection = Edit(ui, null);
                if (connection is null) continue;
                if (index == connections.Count + 1)
                {
                    connections.Add(connection);
                    store.Save(connections);
                    continue;
                }
                return Connect(ui, connection, token);
            }
            var selected = connections[index.Value];
            switch (ui.ShowMenu(selected.DisplayName, ["Connect", "Edit connection...", "Delete connection..."], 0))
            {
                case 0: return Connect(ui, selected, token);
                case 1:
                    var edited = Edit(ui, selected);
                    if (edited is not null) { connections[index.Value] = edited; store.Save(connections); }
                    break;
                case 2:
                    if (ui.Confirm("Delete connection", "Remove saved settings?", selected.DisplayName))
                    { connections.RemoveAt(index.Value); store.Save(connections); }
                    break;
            }
        }
    }

    private static AzureBlobConnection? Edit(IPluginUiService ui, AzureBlobConnection? old)
    {
        string? name = ui.Input("Azure connection", "Display name (not a secret)", old?.DisplayName ?? "Azure");
        if (string.IsNullOrWhiteSpace(name)) return null;
        int? choice = ui.ShowMenu("Authentication", ["Connection string (environment)", "SAS token (environment)",
            "Account or container SAS URL (environment)", "Microsoft Entra ID (Azure CLI)", "Azurite"], (int)(old?.AuthenticationMode ?? AuthenticationMode.ConnectionString));
        if (choice is null) return null;
        var mode = (AuthenticationMode)choice.Value;
        string? endpoint = null, reference = null, container = null;
        if (mode is AuthenticationMode.SasToken or AuthenticationMode.AzureCli)
        {
            endpoint = ui.Input("Azure connection", "Storage account endpoint, without SAS", old?.AccountEndpoint);
            if (endpoint is null) return null;
        }
        if (mode != AuthenticationMode.AzureCli)
        {
            reference = ui.Input("Azure connection", mode == AuthenticationMode.Azurite
                ? "Environment variable for custom connection string (empty = default Azurite)"
                : "Name of environment variable containing credential (never enter the credential itself)",
                old?.CredentialReference ?? (mode == AuthenticationMode.Azurite ? "" : "AZURE_STORAGE_CONNECTION"));
            if (reference is null || (reference.Length == 0 && mode != AuthenticationMode.Azurite)) return null;
            if (reference.Length == 0) reference = null;
        }
        {
            // Also permits an explicit container for an account SAS URL.
            container = ui.Input("Azure connection", "Container (empty = choose on connect or use SAS URL container)", old?.ContainerName ?? "");
            if (container is null) return null;
            if (container.Length == 0) container = null;
        }
        string? initial = ui.Input("Azure connection", "Initial directory", old?.InitialDirectory ?? "/");
        if (initial is null) return null;
        int? access = ui.ShowMenu("Access", ["Read/write (new files only)", "Read-only"], old?.ReadOnly == true ? 1 : 0);
        if (access is null) return null;
        var result = new AzureBlobConnection(old?.Id ?? Guid.NewGuid(), name, mode, endpoint, container,
            AzureBlobPath.Normalize(initial), reference, access == 1);
        AzureBlobClientFactory.Validate(result);
        return result;
    }

    private static PluginPanelOpenRequest? Connect(IPluginUiService ui, AzureBlobConnection connection, CancellationToken token)
    {
        var (service, container) = AzureBlobClientFactory.Create(connection);
        if (container is null)
        {
            var names = new List<string>();
            try
            {
                foreach (var item in service!.GetBlobContainers(cancellationToken: token)) names.Add(item.Name);
            }
            catch (RequestFailedException error) when (error.Status is 401 or 403) { }
            var entries = names.Concat(["Enter container name..."]).ToArray();
            int? index = ui.ShowMenu("Azure containers", entries, 0);
            if (index is null) return null;
            string? name = index == names.Count ? ui.Input("Azure containers", "Container name") : names[index.Value];
            if (string.IsNullOrWhiteSpace(name)) return null;
            AzureBlobClientFactory.Validate(connection with { ContainerName = name });
            container = service!.GetBlobContainerClient(name);
        }
        var source = new AzureBlobFilePanelSource(container, connection.DisplayName, connection.ReadOnly);
        return new PluginPanelOpenRequest
        {
            Source = source, InitialDirectory = AzureBlobPath.Normalize(connection.InitialDirectory),
            Title = connection.DisplayName + " / " + container.Name, Format = "Azure Blob"
        };
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
        OperationCanceledException => "Azure operation cancelled.",
        AzureOperationException => error.Message,
        _ => "Azure operation failed. Check connection settings, path and permissions."
    };

    public static Exception Safe(Exception error) => error is OperationCanceledException cancelled
        ? new OperationCanceledException("Azure operation cancelled.", cancelled.CancellationToken)
        : new AzureOperationException(Message(error));
}

internal sealed class AzureOperationException(string message) : IOException(message);
