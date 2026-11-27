using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Plugin.AzureBlob;

internal static class AzureBlobConnectionDialog
{
    private static readonly string[] Modes =
    [
        "Connection string (environment)",
        "SAS token (environment)",
        "Account or container SAS URL (environment)",
        "Microsoft Entra ID (Azure CLI)",
        "Azurite"
    ];

    internal sealed record Result(AzureBlobConnection Connection, bool Save, PluginPanelOpenRequest? Panel, bool Delete = false);

    public static Result? Show(IPluginUiService ui, AzureBlobConnection? old, bool saveByDefault, string diagnosticsDirectory, CancellationToken token)
    {
        Guid id = old?.Id ?? Guid.NewGuid();
        var name = new PluginFormText("name", "Display name:", old?.DisplayName ?? "Azure");
        var mode = new PluginFormSelect("mode", "Method:", Modes, Modes[(int)(old?.AuthenticationMode ?? AuthenticationMode.ConnectionString)]);
        var endpoint = new PluginFormText("endpoint", "Account endpoint:", old?.AccountEndpoint ?? "");
        var reference = new PluginFormText("reference", "Environment variable:", old?.CredentialReference ?? DefaultReference(AuthenticationMode.ConnectionString));
        var hint = new PluginFormLabel("hint", "");
        var container = new PluginFormText("container", "Container:", old?.ContainerName ?? "")
        {
            Actions = [new("browse", "Browse...", 'B', PluginFormButtonRole.Auxiliary)]
        };
        var directory = new PluginFormText("directory", "Initial directory:", old?.InitialDirectory ?? "/");
        var readOnly = new PluginFormCheckBox("readonly", "Read-only (otherwise new files only)", old?.ReadOnly ?? false);
        var save = new PluginFormCheckBox("save-profile", "Save connection when connecting", saveByDefault);
        var references = new Dictionary<AuthenticationMode, string>();
        var activeMode = old?.AuthenticationMode ?? AuthenticationMode.ConnectionString;
        if (old is not null) reference.Value = old.CredentialReference ?? "";
        references[activeMode] = reference.Value;
        Result? accepted = null;
        AzureErrorInfo? lastError = null;
        PluginForm form = null!;

        void RefreshMode()
        {
            var selected = (AuthenticationMode)Array.IndexOf(Modes, mode.Value);
            if (selected != activeMode)
            {
                references[activeMode] = reference.Value;
                activeMode = selected;
                reference.Value = references.GetValueOrDefault(activeMode, DefaultReference(activeMode));
            }
            endpoint.Visible = activeMode is AuthenticationMode.SasToken or AuthenticationMode.AzureCli;
            reference.Visible = activeMode != AuthenticationMode.AzureCli;
            hint.Text = activeMode switch
            {
                AuthenticationMode.AzureCli => "Uses az login; standard Homebrew locations are detected on macOS.",
                AuthenticationMode.Azurite => "Empty variable = default local Azurite.",
                _ => "Enter the variable name, never the secret itself."
            };
        }

        PluginFormFeedback? Read(out AzureBlobConnection connection)
        {
            connection = null!;
            if (string.IsNullOrWhiteSpace(name.Value))
                return new("Display name is required.", name.Id);
            string? env = reference.Visible && reference.Value.Length > 0 ? reference.Value : null;
            if (reference.Visible && activeMode != AuthenticationMode.Azurite && env is null)
                return new("Enter the credential environment variable name.", reference.Id);
            if (env is not null && env.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
                return new("Enter an environment variable name, not a credential.", reference.Id);
            string? address = endpoint.Visible ? endpoint.Value : null;
            if (endpoint.Visible)
            {
                try { _ = AzureBlobClientFactory.Endpoint(address); }
                catch (ArgumentException) { return new("Use an HTTPS endpoint without credentials or query (HTTP only for loopback).", endpoint.Id); }
            }
            string? selectedContainer = container.Value.Length == 0 ? null : container.Value;
            if (selectedContainer is not null && selectedContainer.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '$'))
                return new("Invalid container name.", container.Id);
            string initial;
            try { initial = AzureBlobPath.Normalize(directory.Value); }
            catch (IOException) { return new("Invalid initial directory.", directory.Id); }
            connection = new(id, name.Value.Trim(), activeMode, address, selectedContainer, initial, env, readOnly.Value);
            return null;
        }

        PluginFormFeedback? ChooseContainer(AzureBlobConnection connection, CancellationToken operationToken)
        {
            var (service, fixedContainer) = AzureBlobClientFactory.Create(connection with { ContainerName = null });
            if (fixedContainer is not null)
            {
                container.Value = fixedContainer.Name;
                return null;
            }
            string[] names = service!.GetBlobContainers(cancellationToken: operationToken).Select(item => item.Name).ToArray();
            if (names.Length == 0) return new("No containers found. Enter the container name directly.", container.Id);
            int? index = ui.ShowMenu("Azure containers", names, Math.Max(0, Array.IndexOf(names, container.Value)));
            if (index is not null) container.Value = names[index.Value];
            return null;
        }

        PluginFormFeedback? Handle(PluginFormEvent e)
        {
            token.ThrowIfCancellationRequested();
            form.Status = "";
            if (e.Kind == PluginFormEventKind.ValueChanged)
            {
                RefreshMode();
                return null;
            }
            if (e.Kind == PluginFormEventKind.Submit && e.Id == "delete" && old is not null)
            {
                accepted = new(old, false, null, Delete: true);
                return null;
            }
            if (e.Kind == PluginFormEventKind.Action && e.Id == "error-details")
            {
                ui.ShowHelp("Azure Blob error details", lastError?.Details ?? ["No connection error has been recorded."]);
                return null;
            }
            if (Read(out var connection) is { } validation) return validation;
            if (e.Kind == PluginFormEventKind.Submit && e.Id == "save")
            {
                accepted = new(connection, true, null);
                return null;
            }

            // Keep synchronous SDK callbacks bounded; the user can edit and retry after an error.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                if (connection.CredentialReference is { } variable &&
                    string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
                    return new("Credential environment variable is missing.", reference.Id);

                if (e.Kind == PluginFormEventKind.Action && e.Id == "browse")
                    return ChooseContainer(connection, timeout.Token);

                var (service, selectedContainer) = AzureBlobClientFactory.Create(connection);
                if (e.Kind == PluginFormEventKind.Action && e.Id == "test")
                {
                    if (selectedContainer is null)
                    {
                        _ = service!.GetBlobContainers(cancellationToken: timeout.Token).AsPages(pageSizeHint: 1).FirstOrDefault();
                        form.Status = "Account listing succeeded. Select a container to test blob listing.";
                    }
                    else
                    {
                        _ = selectedContainer.GetBlobs(BlobTraits.None, BlobStates.None, prefix: AzureBlobPath.Prefix(connection.InitialDirectory),
                            cancellationToken: timeout.Token).AsPages(pageSizeHint: 1).FirstOrDefault();
                        container.Value = selectedContainer.Name;
                        form.Status = "Blob listing succeeded. Write permissions were not tested.";
                    }
                    return null;
                }

                if (e.Kind != PluginFormEventKind.Submit || e.Id != "connect")
                    return new("Unknown connection action.");

                if (selectedContainer is null)
                {
                    if (ChooseContainer(connection, timeout.Token) is { } feedback) return feedback;
                    if (container.Value.Length == 0) return new("Select or enter a container.", container.Id);
                    connection = connection with { ContainerName = container.Value };
                    selectedContainer = AzureBlobClientFactory.Create(connection).Container;
                }
                if (selectedContainer is null) return new("Select or enter a container.", container.Id);
                connection = connection with { ContainerName = selectedContainer.Name };
                // Panel enumeration is asynchronous in the host, as before. Test is an explicit read-only probe.
                var request = new PluginPanelOpenRequest
                {
                    Source = new AzureBlobFilePanelSource(selectedContainer, connection.DisplayName, connection.ReadOnly),
                    InitialDirectory = connection.InitialDirectory,
                    Title = connection.DisplayName + " / " + selectedContainer.Name,
                    Format = "Azure Blob"
                };
                accepted = new(connection, save.Value, request);
                return null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                lastError = AzureErrors.Describe(error);
                AzureBlobDiagnostics.Write(diagnosticsDirectory, lastError);
                return new(lastError.Summary);
            }
        }

        RefreshMode();
        form = new PluginForm("Azure Blob connection",
        [
            new PluginFormLabel("connection-section", "Connection"), name,
            new PluginFormSeparator("auth-separator"),
            new PluginFormLabel("auth-section", "Authentication"), mode, endpoint, reference, hint,
            new PluginFormSeparator("storage-separator"),
            new PluginFormLabel("storage-section", "Storage"), container, directory, readOnly, save
        ])
        {
            PreferredWidth = 80,
            PreferredHeight = 20,
            InitialFocusFieldId = name.Id,
            Buttons =
            [
                new("test", "Test connection", 'T', PluginFormButtonRole.Auxiliary),
                new("save", "Save", 'S'),
                new("connect", "Connect", 'O', IsDefault: true),
                new("error-details", "Error details", 'E', PluginFormButtonRole.Auxiliary),
                .. (old is null ? Array.Empty<PluginFormButton>() : new[] { new PluginFormButton("delete", "Delete", 'D') }),
                new("cancel", "Cancel", 'C', PluginFormButtonRole.Cancel)
            ],
            OnEvent = Handle
        };
        return ui.ShowForm(form, token) is null ? null : accepted;
    }

    private static string DefaultReference(AuthenticationMode mode) => mode switch
    {
        AuthenticationMode.ConnectionString => "AZURE_STORAGE_CONNECTION",
        AuthenticationMode.SasToken => "AZURE_STORAGE_SAS",
        AuthenticationMode.SasUrl => "AZURE_STORAGE_SAS_URL",
        _ => ""
    };
}
