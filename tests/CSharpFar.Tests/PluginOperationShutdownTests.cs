using System.Collections.Concurrent;
using CSharpFar.App.Modules;
using CSharpFar.App.Plugins;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Plugin.Abstractions;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class PluginOperationShutdownTests
{
    [Fact]
    public async Task Shutdown_CancelsRunningCommandBeforeDisposingRuntime()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await Fixture.CreateAsync(host =>
        {
            host.RegisterCommand(new PluginCommandDescriptor
            {
                Id = "wait",
                Title = "Wait",
                ExecuteAsync = async (_, token) =>
                {
                    started.TrySetResult(token);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return PluginCommandResult.Completed();
                }
            });
            host.RegisterCommandPrefix("wait", "wait");
        });

        Assert.True(fixture.Opener.TryOpenFromCommandLine("wait", PanelSide.Right));
        CancellationToken token = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await fixture.Opener.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(token.IsCancellationRequested);
        Assert.Empty(fixture.Errors);
        Assert.Equal(PanelSourceId.Local, fixture.State.SourceId);
    }

    [Fact]
    public async Task Shutdown_DisposesPanelResultAlreadyQueuedOnUiThread()
    {
        var completion = new TaskCompletionSource<PluginCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await Fixture.CreateAsync(host =>
        {
            host.RegisterCommand(new PluginCommandDescriptor
            {
                Id = "pending",
                Title = "Pending",
                ExecuteAsync = (_, _) => new ValueTask<PluginCommandResult>(completion.Task)
            });
            host.RegisterCommandPrefix("pending", "pending");
        });

        var source = new DisposableSource();
        Assert.True(fixture.Opener.TryOpenFromCommandLine("pending", PanelSide.Right));
        completion.SetResult(PluginCommandResult.OpenPanel(Request(source)));
        await fixture.Posted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await fixture.Opener.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));

        await source.Disposed.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, source.DisposeCount);
        Assert.False(fixture.Registry.TryGetSource(source.SourceId, out _));
        Assert.Equal(PanelSourceId.Local, fixture.State.SourceId);
    }

    [Fact]
    public async Task Shutdown_ReturnsIfCommandIgnoresCancellation_AndDiscardsLatePanel()
    {
        var completion = new TaskCompletionSource<PluginCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await Fixture.CreateAsync(host =>
        {
            host.RegisterCommand(new PluginCommandDescriptor
            {
                Id = "ignore",
                Title = "Ignore",
                ExecuteAsync = (_, _) => new ValueTask<PluginCommandResult>(completion.Task)
            });
            host.RegisterCommandPrefix("ignore", "ignore");
        });

        Assert.True(fixture.Opener.TryOpenFromCommandLine("ignore", PanelSide.Right));
        await fixture.Opener.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(fixture.Errors, error => error is TimeoutException);

        var source = new DisposableSource();
        completion.SetResult(PluginCommandResult.OpenPanel(Request(source)));
        await source.Disposed.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, source.DisposeCount);
        Assert.False(fixture.Registry.TryGetSource(source.SourceId, out _));
        Assert.Equal(PanelSourceId.Local, fixture.State.SourceId);
    }

    [Fact]
    public async Task Shutdown_CancelsPendingFileOpenHandler()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await Fixture.CreateAsync(host =>
            host.RegisterFileOpenHandler(new PendingFileHandler(started)));

        var source = new DisposableSource();
        Assert.True(fixture.Registry.TryAddOwned(source));
        var item = new FilePanelItem
        {
            Name = "container.bin",
            FullPath = "/container.bin",
            SourceId = source.SourceId,
            IsDirectory = false
        };

        Assert.True(fixture.Opener.TryOpenFileItem(
            fixture.State, PanelSide.Left, item, (_, _) => throw new Exception("Unexpected fallback")));
        CancellationToken token = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await fixture.Opener.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(token.IsCancellationRequested);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(PanelSourceId.Local, fixture.State.SourceId);
        Assert.Empty(fixture.Errors);
    }

    private static PluginPanelOpenRequest Request(IFilePanelSource source) => new()
    {
        Source = source, InitialDirectory = "/", Title = "Late panel"
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly NativePluginRuntime _runtime;
        private readonly PluginUiActionQueue _queue;
        public FilePanelSourceRegistry Registry { get; } = new([]);
        public FilePanelState State { get; } = new() { CurrentDirectory = Path.GetTempPath() };
        public ConcurrentQueue<Exception> Errors { get; } = new();
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ModulePanelOpener Opener { get; }

        private Fixture(NativePluginRuntime runtime)
        {
            _runtime = runtime;
            _queue = new PluginUiActionQueue(() => { }, Errors.Enqueue);
            var controller = new PanelController(new PanelViewBuilder(
                new FakeFileSystemService(), new PanelSortService(), sources: Registry));
            Opener = new ModulePanelOpener(new NativeModuleCatalog(runtime),
                Registry, controller, null!, null!, null!,
                () => new AppSettings.PanelOptionsSettings(),
                _ => State, _ => { }, _ => { },
                postToUi: action =>
                {
                    _queue.Post(action);
                    Posted.TrySetResult();
                },
                reportCleanupError: Errors.Enqueue,
                showMessage: (_, _) => { },
                drainUiActions: _queue.Drain,
                reportOperationError: Errors.Enqueue);
        }

        public static async Task<Fixture> CreateAsync(Action<IPluginHost> initialize)
        {
            var runtime = new NativePluginRuntime(new TestApplication(),
                _ => new TestSettings(), _ => new TestUi(), _ => new TestCredentials());
            var plugin = new TestPlugin(initialize);
            var result = await runtime.RegisterAsync(
                new PluginDescriptor { Id = "test-operation", Title = "Test operation" }, plugin);
            Assert.True(result.Success, result.Error);
            runtime.Seal();
            return new Fixture(runtime);
        }

        public async ValueTask DisposeAsync()
        {
            await Opener.ShutdownAsync();
            await _runtime.DisposeAsync();
        }
    }

    private sealed class PendingFileHandler(TaskCompletionSource<CancellationToken> started) : IFileOpenHandler
    {
        public string Id => "pending-open";
        public bool CanOpen(FilePanelItem item) => item.Name == "container.bin";

        public async ValueTask<PluginFileOpenResult> OpenAsync(
            FileOpenContext context, CancellationToken cancellationToken)
        {
            started.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return PluginFileOpenResult.NotHandled();
        }
    }

    private sealed class TestPlugin(Action<IPluginHost> initialize) : ICSharpFarPlugin
    {
        public ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken)
        {
            initialize(host);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestApplication : IPluginApplicationContext
    {
        public PluginRunMode RunMode => PluginRunMode.Normal;
        public PanelSide ActivePanelSide => PanelSide.Left;
        public string CurrentDirectory => "/";
    }

    private sealed class TestSettings : IPluginSettingsService
    {
        public string GetSettingsDirectory(string pluginId) => Path.GetTempPath();
    }

    private sealed class TestUi : IPluginUiService
    {
        public void ShowMessage(string title, string message) { }
        public string? Input(string title, string prompt, string? initialText = null) => null;
        public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) => null;
    }

    private sealed class TestCredentials : IPluginCredentialService
    {
        public string? TryReadPassword(string credentialId) => null;
        public void SavePassword(string credentialId, string password) { }
        public void DeletePassword(string credentialId) { }
    }

    private sealed class DisposableSource : IFilePanelSource, IDisposable
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Disposed => _disposed.Task;
        public int DisposeCount { get; private set; }
        public PanelSourceId SourceId { get; } = new("plugin:test-operation:source");
        public string DisplayName => "Pending";
        public PanelProviderCapabilities Capabilities => PanelProviderCapabilities.Enumerate;
        public IReadOnlyCollection<char> PathSeparators => ['/'];
        public string NormalizePath(string sourcePath) => sourcePath;
        public bool IsRootPath(string sourcePath) => sourcePath == "/";
        public string? GetParentPath(string sourcePath) => null;
        public IReadOnlyList<FilePanelItem> EnumerateDirectory(
            string sourcePath, CancellationToken cancellationToken = default) => [];
        public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default) => null;
        public Task<Stream> OpenReadAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> OpenWriteAsync(string sourcePath, bool overwrite,
            CancellationToken cancellationToken = default) => Task.FromResult<Stream>(Stream.Null);
        public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RenameAsync(string sourcePath, string newSourcePath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
            DisposeCount++;
            _disposed.TrySetResult();
        }
    }
}
