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
            var ui = new ConnectionUi(save ? [1, 4, 1, 0, 0] : [0, 4, 1],
                ["Profile", "", "container", "/"]);
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

    private sealed class ConnectionUi(IEnumerable<int> selections, IEnumerable<string> values) : IPluginUiService
    {
        private readonly Queue<int> menu = new(selections);
        private readonly Queue<string> input = new(values);
        public void ShowMessage(string title, string message) => throw new InvalidOperationException(message);
        public string? Input(string title, string prompt, string? initialText = null) => input.Dequeue();
        public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) => menu.Dequeue();
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
