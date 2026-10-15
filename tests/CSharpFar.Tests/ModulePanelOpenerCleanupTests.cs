using System.Collections.Concurrent;
using CSharpFar.App.Modules;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Plugin.Abstractions;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class ModulePanelOpenerCleanupTests
{
    [Fact]
    public async Task ConflictingSource_AsyncDisposalDoesNotBlockAndPreservesPanel()
    {
        var fixture = new Fixture();
        var registered = new SyncSource();
        Assert.True(fixture.Registry.TryAddOwned(registered));
        var rejected = new AsyncSource();

        fixture.Opener.OpenPanel(PanelSide.Right, Request(rejected));

        Assert.Equal(1, rejected.DisposeCalls);
        Assert.False(rejected.Disposed.IsCompleted);
        Assert.Same(registered, fixture.Registry.GetSource(registered.SourceId));
        Assert.Equal(fixture.InitialLocation, fixture.State.CurrentLocation);
        Assert.Equal(PanelSide.Left, fixture.ActiveSide);
        Assert.True(fixture.QuickView);
        Assert.Contains(fixture.Messages, message => message.Contains("already in use", StringComparison.Ordinal));

        Task shutdown = fixture.Opener.AwaitPendingCleanupsAsync();
        Assert.False(shutdown.IsCompleted);
        rejected.CompleteDisposal();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, rejected.DisposeCalls);
        Assert.Empty(fixture.Errors);
    }

    [Fact]
    public async Task FailedAsyncDisposal_IsDiagnosedAndDoesNotReplaceOriginalError()
    {
        var fixture = new Fixture();
        var registered = new SyncSource();
        Assert.True(fixture.Registry.TryAddOwned(registered));
        var rejected = new AsyncSource();
        fixture.Opener.OpenPanel(PanelSide.Right, Request(rejected));

        rejected.FailDisposal(new IOException("cleanup failed"));
        await fixture.Opener.AwaitPendingCleanupsAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var error = Assert.Single(fixture.Errors);
        Assert.Contains("cleanup failed", error.Message, StringComparison.Ordinal);
        Assert.Single(fixture.Messages);
        Assert.Same(registered, fixture.Registry.GetSource(registered.SourceId));
        Assert.Equal(fixture.InitialLocation, fixture.State.CurrentLocation);
    }

    [Fact]
    public async Task CompatibleSource_ReusesRegisteredInstanceAndDisposesIncomingOnce()
    {
        var fixture = new Fixture();
        var existing = new SyncSource { AllowReuse = true };
        Assert.True(fixture.Registry.TryAddOwned(existing));
        var incoming = new AsyncSource();
        fixture.Opener.OpenPanel(PanelSide.Right, Request(incoming));

        Assert.Same(existing, fixture.Registry.GetSource(existing.SourceId));
        Assert.Equal(existing.SourceId, fixture.State.SourceId);
        Assert.Equal(PanelSide.Right, fixture.ActiveSide);
        Assert.False(fixture.QuickView);
        Assert.Empty(fixture.Messages);
        Assert.Equal(0, existing.DisposeCalls);
        Assert.Equal(1, incoming.DisposeCalls);

        incoming.CompleteDisposal();
        await fixture.Opener.AwaitPendingCleanupsAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, existing.DisposeCalls);
        Assert.Equal(1, incoming.DisposeCalls);
    }

    [Fact]
    public async Task FailedInitialLoad_UnregistersSourceAndAwaitsItsCleanup()
    {
        var fixture = new Fixture();
        var source = new AsyncSource { FailLoad = true };
        fixture.Opener.OpenPanel(PanelSide.Right, Request(source));

        Assert.False(fixture.Registry.TryGetSource(source.SourceId, out _));
        Assert.Equal(1, source.DisposeCalls);
        Assert.Equal(fixture.InitialLocation, fixture.State.CurrentLocation);
        Assert.Equal(PanelSide.Left, fixture.ActiveSide);
        Assert.True(fixture.QuickView);
        Assert.Contains(fixture.Messages, message => message.Contains("not changed", StringComparison.Ordinal));
        Task shutdown = fixture.Opener.AwaitPendingCleanupsAsync();
        Assert.False(shutdown.IsCompleted);

        source.CompleteDisposal();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, source.DisposeCalls);
        Assert.Empty(fixture.Errors);
    }

    [Fact]
    public async Task ConflictingSyncDisposable_IsDisposedOnce()
    {
        var fixture = new Fixture();
        var existing = new SyncSource();
        var incoming = new SyncSource();
        Assert.True(fixture.Registry.TryAddOwned(existing));

        fixture.Opener.OpenPanel(PanelSide.Right, Request(incoming));

        Assert.Equal(1, incoming.DisposeCalls);
        Assert.Equal(0, existing.DisposeCalls);
        Assert.Same(existing, fixture.Registry.GetSource(existing.SourceId));
        await fixture.Opener.AwaitPendingCleanupsAsync();
        Assert.Equal(1, incoming.DisposeCalls);
    }

    private static PluginPanelOpenRequest Request(IFilePanelSource source) => new()
    {
        Source = source, InitialDirectory = "/", Title = "Test panel",
    };

    private sealed class Fixture
    {
        public FilePanelSourceRegistry Registry { get; } = new([]);
        public FilePanelState State { get; } = new() { CurrentDirectory = Path.GetTempPath() };
        public ConcurrentQueue<Exception> Errors { get; } = new();
        public List<string> Messages { get; } = [];
        public PanelSide ActiveSide { get; private set; } = PanelSide.Left;
        public bool QuickView { get; private set; } = true;
        public PanelLocation InitialLocation => PanelLocation.Local(Path.GetTempPath());
        public ModulePanelOpener Opener { get; }

        public Fixture()
        {
            var controller = new PanelController(new PanelViewBuilder(
                new FakeFileSystemService(), new PanelSortService(), sources: Registry));
            Opener = new ModulePanelOpener(
                null!, Registry, controller, null!, null!, null!,
                () => new AppSettings.PanelOptionsSettings(),
                _ => State,
                side => ActiveSide = side,
                quickView => QuickView = quickView,
                reportCleanupError: Errors.Enqueue,
                showMessage: (_, message) => Messages.Add(message));
        }
    }

    private class PanelSource : IFilePanelSource, IReusableFilePanelSource
    {
        public bool AllowReuse { get; init; }
        public bool FailLoad { get; init; }
        public PanelSourceId SourceId => new("plugin:test:connections");
        public string DisplayName => "Test";
        public PanelProviderCapabilities Capabilities => PanelProviderCapabilities.Enumerate;
        public IReadOnlyCollection<char> PathSeparators => ['/'];
        public string NormalizePath(string sourcePath) => sourcePath;
        public bool IsRootPath(string sourcePath) => sourcePath == "/";
        public string? GetParentPath(string sourcePath) => null;
        public bool CanReuse(IFilePanelSource candidate) => AllowReuse && candidate.SourceId == SourceId;
        public IReadOnlyList<FilePanelItem> EnumerateDirectory(
            string sourcePath, CancellationToken cancellationToken = default)
        {
            if (FailLoad)
                throw new IOException("Unable to read test panel.");
            return [];
        }
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
    }

    private sealed class AsyncSource : PanelSource, IAsyncDisposable
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCalls { get; private set; }
        public Task Disposed => _completion.Task;
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return new ValueTask(_completion.Task);
        }
        public void CompleteDisposal() => _completion.SetResult();
        public void FailDisposal(Exception error) => _completion.SetException(error);
    }

    private sealed class SyncSource : PanelSource, IDisposable
    {
        public int DisposeCalls { get; private set; }
        public void Dispose() => DisposeCalls++;
    }
}
