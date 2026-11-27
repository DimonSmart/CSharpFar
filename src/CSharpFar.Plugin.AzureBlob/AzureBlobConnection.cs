using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;

namespace CSharpFar.Plugin.AzureBlob;

public enum AuthenticationMode { ConnectionString, SasToken, SasUrl, AzureCli, Azurite }

public sealed record AzureBlobConnection(
    Guid Id, string DisplayName, AuthenticationMode AuthenticationMode,
    string? AccountEndpoint, string? ContainerName, string InitialDirectory,
    string? CredentialReference, bool ReadOnly);

public sealed class AzureBlobConnectionStore(string directory)
{
    private string FileName => Path.Combine(directory, "connections.json");
    private sealed record Settings(int Version, List<AzureBlobConnection> Connections);

    public List<AzureBlobConnection> Load()
    {
        if (!File.Exists(FileName)) return [];
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FileName));
        if (settings?.Version != 1) throw new IOException("Unsupported Azure Blob connection settings version.");
        foreach (var connection in settings.Connections) AzureBlobClientFactory.Validate(connection);
        return settings.Connections;
    }

    public void Save(List<AzureBlobConnection> connections)
    {
        foreach (var connection in connections) AzureBlobClientFactory.Validate(connection);
        Directory.CreateDirectory(directory);
        string temporary = FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(1, connections),
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FileName, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}

public static class AzureBlobClientFactory
{
    public static void Validate(AzureBlobConnection connection)
    {
        if (!Enum.IsDefined(connection.AuthenticationMode) || connection.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(connection.DisplayName))
            throw new ArgumentException("Invalid connection settings.");
        if (connection.CredentialReference is string reference &&
            (reference.Length == 0 || reference.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
            throw new ArgumentException("Credential reference must be an environment variable name.");
        if (connection.AccountEndpoint is not null) _ = Endpoint(connection.AccountEndpoint);
        if (connection.ContainerName is string container &&
            (container.Length == 0 || container.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '$')))
            throw new ArgumentException("Invalid container name.");
        _ = AzureBlobPath.Normalize(connection.InitialDirectory);
    }

    internal static Uri Endpoint(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new ArgumentException("Use an HTTPS storage endpoint (HTTP only for loopback), without credentials or query.");
        return uri;
    }

    public static (BlobServiceClient? Service, BlobContainerClient? Container) Create(AzureBlobConnection connection)
    {
        Validate(connection);
        // Older Azurite releases do not support the latest Azure service API version.
        var options = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2023_11_03);
        options.Diagnostics.IsLoggingContentEnabled = false;
        options.Diagnostics.IsLoggingEnabled = false;
        options.Diagnostics.IsDistributedTracingEnabled = false;
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(15);
        options.Retry.MaxRetries = 2;
        string Secret() => Environment.GetEnvironmentVariable(connection.CredentialReference ?? "")
            is { Length: > 0 } secret ? secret : throw new ArgumentException("Credential environment variable is missing.");
        if (connection.AuthenticationMode == AuthenticationMode.SasUrl)
        {
            if (!Uri.TryCreate(Secret(), UriKind.Absolute, out var url) ||
                (url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback)) ||
                url.UserInfo.Length != 0 || url.Fragment.Length != 0 || url.Query.Length == 0)
                throw new ArgumentException("Expected an account or container SAS URL in the environment variable.");
            var builder = new BlobUriBuilder(url);
            if (!string.IsNullOrEmpty(builder.BlobName))
                throw new ArgumentException("Expected an account or container SAS URL, not a blob URL.");
            if (!string.IsNullOrEmpty(builder.BlobContainerName))
            {
                if (connection.ContainerName is not null && connection.ContainerName != builder.BlobContainerName)
                    throw new ArgumentException("Configured container does not match the SAS URL.");
                return (null, new BlobContainerClient(url, options));
            }
            var sasService = new BlobServiceClient(url, options);
            return (sasService, connection.ContainerName is null ? null : sasService.GetBlobContainerClient(connection.ContainerName));
        }
        BlobServiceClient service = connection.AuthenticationMode switch
        {
            AuthenticationMode.ConnectionString => new BlobServiceClient(Secret(), options),
            AuthenticationMode.Azurite => new BlobServiceClient(connection.CredentialReference is null
                ? "UseDevelopmentStorage=true" : Secret(), options),
            AuthenticationMode.SasToken => new BlobServiceClient(Endpoint(connection.AccountEndpoint),
                new AzureSasCredential(Secret().TrimStart('?')), options),
            AuthenticationMode.AzureCli => new BlobServiceClient(Endpoint(connection.AccountEndpoint),
                new AzureCliCredential(), options),
            _ => throw new ArgumentException("Unknown authentication mode.")
        };
        return (service, connection.ContainerName is null ? null : service.GetBlobContainerClient(connection.ContainerName));
    }
}
