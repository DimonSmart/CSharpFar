using Azure.Storage.Blobs;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Plugin.AzureBlob.Tests;

public sealed class ContractTests
{
    [Theory]
    [InlineData("/", "/", "", null)]
    [InlineData("abc", "/abc", "abc", "/")]
    [InlineData("/a/B/", "/a/B/", "B", "/a/")]
    [InlineData("/Unicode/ñ x%20", "/Unicode/ñ x%20", "ñ x%20", "/Unicode/")]
    public void PathPreservesNames(string input, string normalized, string name, string? parent)
    {
        Assert.Equal(normalized, AzureBlobPath.Normalize(input));
        Assert.Equal(name, AzureBlobPath.Name(input));
        Assert.Equal(parent, AzureBlobPath.Parent(input));
    }

    [Theory]
    [InlineData("/a//b")]
    [InlineData("//a")]
    [InlineData("/a/../b")]
    [InlineData("/a/./b")]
    [InlineData("/a\\b")]
    public void UnsafePathsAreRejected(string input) => Assert.Throws<IOException>(() => AzureBlobPath.Normalize(input));

    [Fact]
    public void IdentityAndCapabilities()
    {
        var a = new AzureBlobFilePanelSource(new BlobContainerClient(new Uri("https://example.blob.core.windows.net/data?sig=one")), "A", false);
        var b = new AzureBlobFilePanelSource(new BlobContainerClient(new Uri("https://example.blob.core.windows.net/data?sig=two")), "B", true);
        Assert.Equal(a.PhysicalIdentity, b.PhysicalIdentity);
        Assert.DoesNotContain("sig=", a.PhysicalIdentity);
        Assert.NotEqual(a.SourceId, b.SourceId);
        Assert.False(a.PathComparer.Equals("/A", "/a"));
        Assert.True(a.Capabilities.HasFlag(PanelProviderCapabilities.CopyTo));
        Assert.False(b.Capabilities.HasFlag(PanelProviderCapabilities.CopyTo));
        Assert.False(a.Capabilities.HasFlag(PanelProviderCapabilities.Edit));
        Assert.IsAssignableFrom<ISafeNewFilePublicationProvider>(a);
        Assert.False((object)a is IRecoverableEntryRenameProvider);
        Assert.True(a.IsTemporary);
    }

    [Fact]
    public void SettingsContainReferencesNotSecrets()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var connection = new AzureBlobConnection(Guid.NewGuid(), "Test", AuthenticationMode.SasToken,
                "https://example.blob.core.windows.net", "data", "/", "TEST_AZURE_SAS", true);
            var store = new AzureBlobConnectionStore(directory);
            store.Save([connection]);
            Assert.Equal(connection, Assert.Single(store.Load()));
            var file = File.ReadAllText(Path.Combine(directory, "connections.json"));
            Assert.Contains("TEST_AZURE_SAS", file);
            Assert.Throws<ArgumentException>(() => store.Save([connection with { CredentialReference = "sig=secret" }]));
            Assert.Throws<ArgumentException>(() => store.Save([connection with { AccountEndpoint = "https://example.blob.core.windows.net?sig=secret" }]));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RegistersPublicContractsWithoutNetwork()
    {
        var host = new Host();
        await new Plugin().InitializeAsync(host, default);
        Assert.Equal("azure", host.Prefix);
        Assert.Equal("open", Assert.Single(host.Commands).Id);
        Assert.Contains(host.Menus, m => m.Placement == PluginMenuPlacement.Plugins);
        Assert.Contains(host.Menus, m => m.Placement == PluginMenuPlacement.Disk);
        Assert.IsType<AzureBlobPanelProvider>(Assert.Single(host.Providers));
    }

    [Fact]
    public async Task NetworkFailureIsSanitized()
    {
        var options = new BlobClientOptions();
        options.Retry.MaxRetries = 0;
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(1);
        var source = new AzureBlobFilePanelSource(
            new BlobContainerClient(new Uri("http://127.0.0.1:1/data?sig=VERY_SECRET"), options), "Test", false);
        var error = await Assert.ThrowsAnyAsync<IOException>(() => source.EnumerateDirectoryAsync("/"));
        Assert.DoesNotContain("VERY_SECRET", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task ExplicitCancellationRemainsCancellation()
    {
        var source = new AzureBlobFilePanelSource(
            new BlobContainerClient(new Uri("http://127.0.0.1:1/data?sig=VERY_SECRET")), "Test", false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.EnumerateDirectoryAsync("/", cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.DoesNotContain("VERY_SECRET", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedAndTemporaryConnections(bool save)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            // Azurite client construction is local; an explicit container avoids a network list call.
            var ui = new ConnectionUi([save ? 1 : 0], form =>
            {
                Assert.Equal(save, Field<PluginFormCheckBox>(form, "save-profile").Value);
                Field<PluginFormText>(form, "name").Value = "Profile";
                var mode = Field<PluginFormSelect>(form, "mode");
                mode.Value = "Azurite";
                Assert.Null(form.OnEvent!(new(PluginFormEventKind.ValueChanged, mode.Id)));
                Assert.Equal("", Field<PluginFormText>(form, "reference").Value);
                Field<PluginFormText>(form, "container").Value = "container";
                Field<PluginFormCheckBox>(form, "readonly").Value = true;
                Assert.Null(form.OnEvent!(new(PluginFormEventKind.Submit, "connect")));
                return "connect";
            });
            var result = await new AzureBlobPanelProvider().ExecuteAsync(ui, new SettingsDirectory(directory), default);
            Assert.Equal(PluginCommandResultKind.OpenPanel, result.Kind);
            Assert.IsType<AzureBlobFilePanelSource>(result.Panel!.Source);
            Assert.False(result.Panel.Source.Capabilities.HasFlag(PanelProviderCapabilities.Delete));
            Assert.Equal(save, File.Exists(Path.Combine(directory, "connections.json")));
            if (save) Assert.Equal("Profile", Assert.Single(new AzureBlobConnectionStore(directory).Load()).DisplayName);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class SettingsDirectory(string directory) : IPluginSettingsService
    {
        public string GetSettingsDirectory(string pluginId) => directory;
    }

    [Fact]
    public async Task FormValidatesAndNeverPersistsCancelledDraftOrHiddenCredentials()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var ui = new ConnectionUi([1], form =>
            {
                var mode = Field<PluginFormSelect>(form, "mode");
                var endpoint = Field<PluginFormText>(form, "endpoint");
                var reference = Field<PluginFormText>(form, "reference");
                Assert.False(endpoint.Visible);
                mode.Value = "SAS token (environment)";
                Assert.Null(form.OnEvent!(new(PluginFormEventKind.ValueChanged, mode.Id)));
                Assert.True(endpoint.Visible);
                endpoint.Value = "https://example.blob.core.windows.net?sig=secret";
                Assert.Equal(endpoint.Id, form.OnEvent!(new(PluginFormEventKind.Submit, "save"))!.FocusFieldId);
                endpoint.Value = "https://example.blob.core.windows.net";
                reference.Value = "sig=secret";
                Assert.Equal(reference.Id, form.OnEvent!(new(PluginFormEventKind.Submit, "save"))!.FocusFieldId);
                mode.Value = "Microsoft Entra ID (Azure CLI)";
                Assert.Null(form.OnEvent!(new(PluginFormEventKind.ValueChanged, mode.Id)));
                Assert.False(reference.Visible);
                Field<PluginFormText>(form, "name").Value = "Cancelled";
                return null;
            });
            var result = await new AzureBlobPanelProvider().ExecuteAsync(ui, new SettingsDirectory(directory), default);
            Assert.Equal(PluginCommandResultKind.Cancelled, result.Kind);
            Assert.False(File.Exists(Path.Combine(directory, "connections.json")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SaveDoesNotRequireNetworkAndEditingKeepsProfileIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var ui = new ConnectionUi([1], form =>
            {
                Field<PluginFormText>(form, "name").Value = "Saved";
                Assert.Null(form.OnEvent!(new(PluginFormEventKind.Submit, "save")));
                return "save";
            });
            var store = new AzureBlobConnectionStore(directory);
            await new AzureBlobPanelProvider().ExecuteAsync(ui, new SettingsDirectory(directory), default);
            var saved = Assert.Single(store.Load());
            var edit = new ConnectionUi([0], form =>
            {
                Assert.Equal("Saved", Field<PluginFormText>(form, "name").Value);
                Field<PluginFormText>(form, "name").Value = "Renamed";
                var mode = Field<PluginFormSelect>(form, "mode");
                mode.Value = "Microsoft Entra ID (Azure CLI)";
                Assert.Null(form.OnEvent!(new(PluginFormEventKind.ValueChanged, mode.Id)));
                Field<PluginFormText>(form, "endpoint").Value = "https://example.blob.core.windows.net";
                Assert.Null(form.OnEvent!(new(PluginFormEventKind.Submit, "save")));
                return "save";
            });
            await new AzureBlobPanelProvider().ExecuteAsync(edit, new SettingsDirectory(directory), default);
            var updated = Assert.Single(store.Load());
            Assert.Equal(saved.Id, updated.Id);
            Assert.Equal("Renamed", updated.DisplayName);
            Assert.Null(updated.CredentialReference);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DeletingProfileRequiresConfirmationAndDoesNotValidateDraft()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AzureBlobConnectionStore(directory);
            var profile = new AzureBlobConnection(Guid.NewGuid(), "Saved", AuthenticationMode.Azurite,
                null, null, "/", null, false);
            store.Save([profile]);
            foreach (bool confirm in new[] { false, true })
            {
                var ui = new ConnectionUi([0], form =>
                {
                    Assert.Contains(form.Buttons, button => button.Id == "delete");
                    Field<PluginFormText>(form, "name").Value = "";
                    Assert.Null(form.OnEvent!(new(PluginFormEventKind.Submit, "delete")));
                    return "delete";
                }) { ConfirmDelete = confirm };
                var result = await new AzureBlobPanelProvider().ExecuteAsync(ui, new SettingsDirectory(directory), default);
                Assert.Equal(PluginCommandResultKind.Cancelled, result.Kind);
                if (confirm) Assert.Empty(store.Load());
                else Assert.Equal(profile, Assert.Single(store.Load()));
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [IntegrationFact]
    [Trait("Category", "AzureIntegration")]
    public async Task BrowseAndTestConnectionKeepFormOpenAndDoNotModifyStorage()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new AzureBlobConnection(Guid.NewGuid(), "Test", AuthenticationMode.ConnectionString,
            null, null, "/", "CSHARPFAR_AZURE_TEST_CONNECTION", true);
        var (service, _) = AzureBlobClientFactory.Create(profile);
        var container = service!.GetBlobContainerClient("csharpfar-" + Guid.NewGuid().ToString("N"));
        await container.CreateAsync();
        try
        {
            var ui = new ConnectionUi([0], form =>
            {
                Field<PluginFormText>(form, "reference").Value = profile.CredentialReference!;
                Assert.Null(form.OnEvent!(new(PluginFormEventKind.Action, "browse")));
                Assert.Equal(container.Name, Field<PluginFormText>(form, "container").Value);
                Assert.Null(form.OnEvent!(new(PluginFormEventKind.Action, "test")));
                Assert.Contains("Blob listing succeeded.", form.Status);
                Assert.Null(form.OnEvent!(new(PluginFormEventKind.Submit, "connect")));
                return "connect";
            }) { ContainerToSelect = container.Name };
            var result = await new AzureBlobPanelProvider().ExecuteAsync(ui, new SettingsDirectory(directory), default);
            Assert.Equal(PluginCommandResultKind.OpenPanel, result.Kind);
            Assert.Contains(container.Name, result.Panel!.Title);
            Assert.Empty(container.GetBlobs(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix: null, cancellationToken: default));
            Assert.False(File.Exists(Path.Combine(directory, "connections.json")));
        }
        finally
        {
            await container.DeleteIfExistsAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static T Field<T>(PluginForm form, string id) where T : PluginFormRow =>
        Assert.IsType<T>(Assert.Single(form.Rows, row => row.Id == id));

    private sealed class ConnectionUi(IEnumerable<int> selections, Func<PluginForm, string?> edit) : IPluginUiService
    {
        private readonly Queue<int> menu = new(selections);
        public string? ContainerToSelect { get; init; }
        public bool ConfirmDelete { get; init; }
        public bool Confirm(string title, string question, string itemName) => ConfirmDelete;
        public bool SupportsForms => true;
        public string? ShowForm(PluginForm form, CancellationToken cancellationToken = default) => edit(form);
        public void ShowMessage(string title, string message) => throw new InvalidOperationException(message);
        public string? Input(string title, string prompt, string? initialText = null) =>
            throw new InvalidOperationException("Connection editing must use the form.");
        public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) =>
            title == "Azure containers" && ContainerToSelect is not null
                ? Array.IndexOf(items.ToArray(), ContainerToSelect)
                : menu.TryDequeue(out int value) ? value : null;
    }

    private sealed class Host : IPluginHost
    {
        public List<PluginCommandDescriptor> Commands { get; } = [];
        public List<PluginMenuItemDescriptor> Menus { get; } = [];
        public List<IPanelProvider> Providers { get; } = [];
        public string? Prefix { get; private set; }
        public IPluginApplicationContext Application => throw new NotSupportedException();
        public IPluginSettingsService Settings => throw new NotSupportedException();
        public IPluginUiService Ui => throw new NotSupportedException();
        public IPluginCredentialService Credentials => throw new NotSupportedException();
        public void RegisterCommand(PluginCommandDescriptor command) => Commands.Add(command);
        public void RegisterMenuItem(PluginMenuItemDescriptor menu) => Menus.Add(menu);
        public void RegisterCommandPrefix(string prefix, string commandId) => Prefix = prefix;
        public void RegisterPanelProvider(IPanelProvider provider) => Providers.Add(provider);
    }
}
