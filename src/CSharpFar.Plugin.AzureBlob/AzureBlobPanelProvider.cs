using Azure;
using Azure.Identity;
using System.Text.RegularExpressions;
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

        string settingsDirectory = settings.GetSettingsDirectory("azure-blob");
        var store = new AzureBlobConnectionStore(settingsDirectory);
        var connections = store.Load();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var entries = connections.Select(c => c.DisplayName).Concat(["Quick connect...", "New connection..."]).ToArray();
            int? index = ui.ShowMenu("Azure Blob Storage", entries, 0);
            if (index is null) return null;
            if (index >= connections.Count)
            {
                var draft = AzureBlobConnectionDialog.Show(ui, null, index == connections.Count + 1, settingsDirectory, token);
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
            var edited = AzureBlobConnectionDialog.Show(ui, selected, true, settingsDirectory, token);
            if (edited is null) continue;
            if (edited.Delete)
            {
                if (ui.Confirm("Delete connection", "Remove saved settings?", selected.DisplayName))
                {
                    connections.RemoveAt(index.Value);
                    store.Save(connections);
                }
                continue;
            }
            if (edited.Save)
            {
                connections[index.Value] = edited.Connection;
                store.Save(connections);
            }
            if (edited.Panel is not null) return edited.Panel;
        }
    }
}

internal sealed record AzureErrorInfo(string Summary, IReadOnlyList<string> Details);

internal static class AzureErrors
{
    private static readonly Regex UriQuery = new(@"(?i)(https?://[^\s?]+)\?[^\s]+", RegexOptions.Compiled);
    private static readonly Regex NamedSecret = new(
        @"(?i)(\b(?:sig|accountkey|sharedaccesssignature|access[_-]?token|refreshtoken|client_secret)\b\s*[:=]\s*[""']?)[^&\s,""'}]+",
        RegexOptions.Compiled);

    public static string Message(Exception error) => Describe(error).Summary;

    internal static AzureErrorInfo Describe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var chain = ExceptionChain(error).ToArray();
        string combined = string.Join("\n", chain.Select(item => item.Message));
        string summary = error switch
        {
            RequestFailedException failure => failure.Status switch
            {
                0 => "Azure storage is unreachable. Check network and storage firewall.",
                401 => "Azure authentication failed (HTTP 401). Check the selected credentials.",
                403 => "Azure access denied (HTTP 403). Check Blob Data role and storage firewall.",
                404 => "Azure container or object was not found (HTTP 404).",
                409 => "Azure object conflict (HTTP 409). Refresh before retrying.",
                412 => "Azure object changed or already exists (HTTP 412). Refresh and retry.",
                429 => "Azure storage is throttling requests (HTTP 429). Retry later.",
                _ => $"Azure request failed (HTTP {failure.Status}). Open Error details for diagnostics."
            },
            OperationCanceledException => "Azure operation timed out or was interrupted.",
            AzureOperationException => error.Message,
            _ when IsAzureCli(chain, combined) => AzureCliSummary(combined),
            _ => "Azure operation failed. Open Error details for diagnostics."
        };

        var details = new List<string>
        {
            summary,
            "",
            "Exception chain:"
        };
        foreach (Exception item in chain)
            details.Add($"{item.GetType().Name}: {Sanitize(item.Message)}");

        if (IsAzureCli(chain, combined))
        {
            var path = AzureCliSupport.InspectCurrentPath();
            details.Add("");
            details.Add(path.ExecutablePath is null
                ? "Azure CLI executable: not found in the current process PATH or standard macOS locations."
                : "Azure CLI executable: found.");
            if (OperatingSystem.IsMacOS())
                details.Add($"Standard Homebrew directory added to credential PATH: {(path.AddedStandardPath ? "yes" : "no")}.");
            details.Add("Verify in Terminal: az account get-access-token --resource https://storage.azure.com/");
            details.Add("If authentication is stale or MFA is required, run: az login");
        }

        return new(summary, details);
    }

    public static Exception Safe(Exception error, CancellationToken token = default) =>
        error is OperationCanceledException && token.IsCancellationRequested
            ? new OperationCanceledException("Azure operation cancelled.", token)
            : new AzureOperationException(Message(error));

    private static IEnumerable<Exception> ExceptionChain(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            yield return current;
    }

    private static bool IsAzureCli(IReadOnlyList<Exception> chain, string combined) =>
        chain.Any(item => item is AuthenticationFailedException or CredentialUnavailableException) ||
        combined.Contains("Azure CLI", StringComparison.OrdinalIgnoreCase) ||
        combined.Contains("az login", StringComparison.OrdinalIgnoreCase) ||
        combined.Contains("az:", StringComparison.OrdinalIgnoreCase);

    private static string AzureCliSummary(string message)
    {
        if (ContainsAny(message, "Azure CLI not installed", "az: not found", "command not found", "'az' is not recognized"))
            return "Azure CLI was not found. Install it or make az available to CSharpFar.";
        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "Azure CLI token request timed out. Check az in Terminal and retry.";
        if (ContainsAny(message, "multi-factor", "claims challenge", "AADSTS50076", "AADSTS50079"))
            return "Azure CLI needs re-authentication or MFA. Run az login, then retry.";
        if (ContainsAny(message, "Please run 'az login'", "Interactive login is required"))
            return "Azure CLI is not signed in. Run az login, then retry.";
        return "Azure CLI authentication failed. Open Error details for the CLI reason.";
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    internal static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message)) return "";
        string safe = UriQuery.Replace(message, "$1?<redacted>");
        safe = NamedSecret.Replace(safe, "$1<redacted>");
        return safe.Length <= 1200 ? safe : safe[..1200] + "…";
    }
}

internal static class AzureBlobDiagnostics
{
    private const string LogFileName = "azure-blob-diagnostics.log";
    private const long MaxLogBytes = 512 * 1024;

    internal static void Write(string settingsDirectory, AzureErrorInfo error)
    {
        try
        {
            Directory.CreateDirectory(settingsDirectory);
            string file = Path.Combine(settingsDirectory, LogFileName);
            string entry = $"[{DateTimeOffset.UtcNow:O}] {string.Join(Environment.NewLine, error.Details)}{Environment.NewLine}{Environment.NewLine}";
            if (File.Exists(file) && new FileInfo(file).Length > MaxLogBytes)
                File.WriteAllText(file, entry);
            else
                File.AppendAllText(file, entry);
        }
        catch
        {
            // Diagnostics must never replace the original connection failure.
        }
    }
}

internal sealed class AzureOperationException(string message) : IOException(message);
