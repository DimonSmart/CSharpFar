using CSharpFar.App.Modules;
using CSharpFar.App.Plugins;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Tests;

public sealed class NativePluginIntegrationTests
{
    [Fact]
    public async Task IndependentPlugin_RegistersMenuDiskPrefixAndVirtualPanel()
    {
        var ui = new TestUi();
        var app = new TestApp { RunMode = PluginRunMode.Demo };
        var runtime = new NativePluginRuntime(
            app, _ => new TestSettings(), _ => ui, _ => new NoCredentials());
        var source = new VirtualPanelSource();
        var plugin = new IndependentPlugin(source);

        var registration = await runtime.RegisterAsync(
            new PluginDescriptor { Id = "outside", Title = "Independent" }, plugin);
        Assert.True(registration.Success, registration.Error);
        runtime.Seal();

        var catalog = new NativeModuleCatalog(runtime);
        Assert.Single(catalog.MenuItems);
        Assert.Single(catalog.DiskMenuItems);
        Assert.Equal("Independent plugin", catalog.MenuItems.Single().Text);
        Assert.Equal("Independent disk", catalog.DiskMenuItems.Single().Text);
        Assert.True(catalog.TryOpenFromCommandLine("HeLlO:exact arg", PanelSide.Right, out var invoked));
        Assert.Equal(PluginCommandResultKind.OpenPanel, (await invoked).Kind);
        Assert.Equal(PanelSide.Right, plugin.LastCommandSide);
        Assert.Equal("HeLlO:exact arg", plugin.LastRawLine);
        Assert.Equal("exact arg", plugin.LastArguments);

        var fromDisk = await catalog.OpenFromDiskMenu(catalog.DiskMenuItems.Single().ActionId, PanelSide.Left);
        Assert.Equal(PluginCommandResultKind.OpenPanel, fromDisk.Kind);
        Assert.Equal(PanelSide.Left, plugin.LastCommandSide);
        Assert.Null(plugin.LastRawLine);

        var provider = await runtime.OpenProviderAsync("outside", "virtual", PanelSide.Right);
        Assert.Equal(PluginCommandResultKind.OpenPanel, provider.Kind);
        Assert.Same(source, provider.Panel!.Source);
        Assert.Equal("Demo Virtual", provider.Panel.Title);
        Assert.Equal("/", provider.Panel.InitialDirectory);
        Assert.Equal(PanelSide.Right, plugin.LastProviderSide);
        Assert.Equal(PluginRunMode.Demo, plugin.LastRunMode);
        Assert.Equal("shown", Assert.Single(ui.Messages));
        Assert.False(catalog.TryOpenFromCommandLine("not-a-plugin argument", PanelSide.Left, out _));
    }

    [Fact]
    public void PanelRegistry_RejectsSilentReplacementAndDisposesOwnedSourceOnce()
    {
        var source = new VirtualPanelSource();
        var collision = new VirtualPanelSource();
        var registry = new FilePanelSourceRegistry(Array.Empty<IFilePanelSource>());
        Assert.True(registry.TryAddOwned(source));
        Assert.False(registry.TryAddOwned(collision));
        Assert.Same(source, registry.GetSource(source.SourceId));
        Assert.False(registry.RemoveIfSame(collision.SourceId, collision));
        registry.DisposeOwnedSources();
        registry.DisposeOwnedSources();

        Assert.Equal(1, source.DisposalCount);
        Assert.Equal(0, collision.DisposalCount);
        Assert.False(registry.TryGetSource(source.SourceId, out _));
    }

    [Fact]
    public async Task AsyncCompletionQueue_ExecutesOnlyWhenInputThreadDrains()
    {
        int wakeCount = 0;
        int calledOn = 0;
        var queue = new PluginUiActionQueue(() => Interlocked.Increment(ref wakeCount));
        await Task.Run(() => queue.Post(() => calledOn = Environment.CurrentManagedThreadId));
        Assert.Equal(0, calledOn);
        Assert.Equal(1, wakeCount);
        int drainThread = Environment.CurrentManagedThreadId;
        queue.Drain();
        Assert.Equal(drainThread, calledOn);
    }

    private sealed class IndependentPlugin(VirtualPanelSource source) : ICSharpFarPlugin
    {
        public PanelSide LastCommandSide { get; private set; }
        public PanelSide LastProviderSide { get; private set; }
        public string? LastRawLine { get; private set; }
        public string? LastArguments { get; private set; }
        public PluginRunMode LastRunMode { get; private set; }

        public ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken)
        {
            LastRunMode = host.Application.RunMode;
            host.Ui.ShowMessage("Independent", "shown");
            Assert.NotEmpty(host.Settings.GetSettingsDirectory("outside"));
            host.RegisterMenuItem(new PluginMenuItemDescriptor
            {
                Id = "menu", Title = "Independent plugin", CommandId = "open",
            });
            host.RegisterMenuItem(new PluginMenuItemDescriptor
            {
                Id = "disk", Title = "Independent disk", CommandId = "open",
                Placement = PluginMenuPlacement.Disk,
            });
            host.RegisterCommandPrefix("hello", "open");
            host.RegisterCommand(new PluginCommandDescriptor
            {
                Id = "open", Title = "Virtual open",
                ExecuteAsync = (context, _) =>
                {
                    LastCommandSide = context.TargetPanelSide;
                    LastRawLine = context.RawCommandLine;
                    LastArguments = context.Arguments;
                    return ValueTask.FromResult(PluginCommandResult.OpenPanel(new PluginPanelOpenRequest
                    {
                        Source = source,
                        InitialDirectory = "/",
                        Title = "Demo Virtual",
                    }));
                },
            });
            host.RegisterPanelProvider(new VirtualProvider(this, source));
            return ValueTask.CompletedTask;
        }

        private sealed class VirtualProvider(IndependentPlugin owner, VirtualPanelSource source) : IPanelProvider
        {
            public string Id => "virtual";
            public string Title => "Virtual";

            public ValueTask<PluginPanelOpenRequest> OpenAsync(
                IPanelProviderContext context, CancellationToken cancellationToken)
            {
                owner.LastProviderSide = context.TargetPanelSide;
                return ValueTask.FromResult(new PluginPanelOpenRequest
                {
                    Source = source, InitialDirectory = "/", Title = "Demo Virtual",
                });
            }
        }
    }

    private sealed class TestApp : IPluginApplicationContext
    {
        public PluginRunMode RunMode { get; init; }
        public PanelSide ActivePanelSide => PanelSide.Left;
        public string CurrentDirectory => "/";
    }

    private sealed class TestSettings : IPluginSettingsService
    {
        public string GetSettingsDirectory(string pluginId) => Path.GetTempPath();
    }

    private sealed class NoCredentials : IPluginCredentialService
    {
        public string? TryReadPassword(string credentialId) => null;
        public void SavePassword(string credentialId, string password) { }
        public void DeletePassword(string credentialId) { }
    }

    private sealed class TestUi : IPluginUiService
    {
        public List<string> Messages { get; } = [];
        public void ShowMessage(string title, string message) => Messages.Add(message);
        public string? Input(string title, string prompt, string? initialText = null) => null;
        public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) => null;
    }

    private sealed class VirtualPanelSource : IFilePanelSource, IDisposable
    {
        public int DisposalCount { get; private set; }
        public PanelSourceId SourceId =>
            PanelSourceId.Module(Guid.Parse("11111111-2222-3333-4444-555555555555"), "virtual");
        public string DisplayName => "Virtual";
        public PanelProviderCapabilities Capabilities => PanelProviderCapabilities.Enumerate;
        public IReadOnlyCollection<char> PathSeparators => ['/'];
        public string NormalizePath(string sourcePath) => sourcePath;
        public bool IsRootPath(string sourcePath) => sourcePath == "/";
        public string? GetParentPath(string sourcePath) => null;
        public IReadOnlyList<FilePanelItem> EnumerateDirectory(string sourcePath,
            CancellationToken cancellationToken = default) => Array.Empty<FilePanelItem>();
        public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default) => null;
        public Task<Stream> OpenReadAsync(string sourcePath,
            CancellationToken cancellationToken = default) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> OpenWriteAsync(string sourcePath, bool overwrite,
            CancellationToken cancellationToken = default) => Task.FromResult<Stream>(Stream.Null);
        public Task CreateDirectoryAsync(string sourcePath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string sourcePath, bool recursive,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RenameAsync(string sourcePath, string newSourcePath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() => DisposalCount++;
    }
}
