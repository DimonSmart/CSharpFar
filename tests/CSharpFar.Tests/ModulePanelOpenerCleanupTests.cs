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


    [Fact]
    public void TemporarySource_IsReleasedOnlyAfterNeitherPanelReferencesIt()
    {
        var registry = new FilePanelSourceRegistry([]);
        var source = new TemporarySource();
        Assert.True(registry.TryAddOwned(source));

        registry.RemoveUnusedTemporarySources(source.SourceId, PanelSourceId.Local, DisposeSource);
        Assert.True(registry.TryGetSource(source.SourceId, out _));
        Assert.Equal(0, source.DisposeCalls);

        registry.RemoveUnusedTemporarySources(PanelSourceId.Local, PanelSourceId.Local, DisposeSource);
        Assert.False(registry.TryGetSource(source.SourceId, out _));
        Assert.Equal(1, source.DisposeCalls);

        registry.DisposeOwnedSources();
        Assert.Equal(1, source.DisposeCalls);
    }

    [Fact]
    public void TemporarySource_ActiveOperationLeaseDelaysRetirement()
    {
        var registry = new FilePanelSourceRegistry([]);
        var source = new TemporarySource();
        Assert.True(registry.TryAddOwned(source));
        var operation = registry.AcquireUsage(source.SourceId);

        registry.RemoveUnusedTemporarySources(PanelSourceId.Local, PanelSourceId.Local, DisposeSource);
        Assert.True(registry.TryGetSource(source.SourceId, out _));
        Assert.Equal(0, source.DisposeCalls);

        operation.Dispose();
        Assert.False(registry.TryGetSource(source.SourceId, out _));
        Assert.Equal(1, source.DisposeCalls);
        operation.Dispose();
        Assert.Equal(1, source.DisposeCalls);
    }

    [Fact]
    public void TemporarySource_TwoIdenticalEndpointsCanHaveIndependentIdentities()
    {
        var registry = new FilePanelSourceRegistry([]);
        var first = new TemporarySource { SourceId = new PanelSourceId("plugin:test:temporary-1") };
        var second = new TemporarySource { SourceId = new PanelSourceId("plugin:test:temporary-2") };
        Assert.True(registry.TryAddOwned(first));
        Assert.True(registry.TryAddOwned(second));

        registry.RemoveUnusedTemporarySources(first.SourceId, PanelSourceId.Local, DisposeSource);
        Assert.True(registry.TryGetSource(first.SourceId, out _));
        Assert.False(registry.TryGetSource(second.SourceId, out _));
        Assert.Equal(1, second.DisposeCalls);

        registry.RemoveUnusedTemporarySources(PanelSourceId.Local, PanelSourceId.Local, DisposeSource);
        Assert.Equal(1, first.DisposeCalls);
    }

    [Fact]
    public void PermanentSource_RemainsRegisteredAfterTemporaryCleanup()
    {
        var registry = new FilePanelSourceRegistry([]);
        var saved = new SyncSource();
        Assert.True(registry.TryAddOwned(saved));
        registry.RemoveUnusedTemporarySources(PanelSourceId.Local, PanelSourceId.Local, DisposeSource);
        Assert.Same(saved, registry.GetSource(saved.SourceId));
        Assert.Equal(0, saved.DisposeCalls);
        registry.DisposeOwnedSources();
        Assert.Equal(1, saved.DisposeCalls);
    }

    [Fact]
    public void TemporarySource_OpenFailureLeavesNoRegistration()
    {
        var fixture = new Fixture();
        var source = new TemporarySource { FailLoad = true };
        fixture.Opener.OpenPanel(PanelSide.Right, Request(source));

        Assert.False(fixture.Registry.TryGetSource(source.SourceId, out _));
        Assert.Equal(1, source.DisposeCalls);
        Assert.Equal(fixture.InitialLocation, fixture.State.CurrentLocation);
    }

    [Fact]
    public void TemporarySource_PanelOpenerReleasesAfterLeavingRemotePanel()
    {
        var fixture = new Fixture();
        var source = new TemporarySource();
        fixture.Opener.OpenPanel(PanelSide.Right, Request(source));
        Assert.True(fixture.Registry.TryGetSource(source.SourceId, out _));

        fixture.State.CurrentDirectory = Path.GetTempPath();
        fixture.Opener.CleanupUnusedTemporarySources();

        Assert.False(fixture.Registry.TryGetSource(source.SourceId, out _));
        Assert.Equal(1, source.DisposeCalls);
    }

    private static void DisposeSource(IFilePanelSource source) => ((IDisposable)source).Dispose();

    private sealed class TemporarySource : SyncSource, ITemporaryFilePanelSource
    {
        public bool IsTemporary => true;
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
        public PanelSourceId SourceId { get; init; } = new("plugin:test:connections");
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

    private class SyncSource : PanelSource, IDisposable
    {
        public int DisposeCalls { get; private set; }
        public void Dispose() => DisposeCalls++;
    }
}
