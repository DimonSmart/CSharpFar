using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

public sealed class FilePanelSourceAsyncTests
{
    [Fact]
    public async Task LegacyOperationsAreBoundedAndDoNotAbandonStartedWork()
    {
        var source = new ControlledLegacySource();
        var asyncSource = ((IFilePanelSource)source).AsAsync();
        using var firstCancellation = new CancellationTokenSource();
        using var queuedCancellation = new CancellationTokenSource();
        Task<IReadOnlyList<FilePanelItem>>? first = null;
        Task<IReadOnlyList<FilePanelItem>>? second = null;
        try
        {
            // StartNew deliberately does not unwrap: calling the SDK method must
            // return even while the underlying synchronous provider is blocked.
            first = await Task.Factory.StartNew(
                () => asyncSource.EnumerateDirectoryAsync("/", firstCancellation.Token),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default)
                .WaitAsync(TimeSpan.FromSeconds(10));
            await source.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(first.IsCompleted);
            second = await Task.Factory.StartNew(
                () => asyncSource.EnumerateDirectoryAsync("/"),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default)
                .WaitAsync(TimeSpan.FromSeconds(10));
            await source.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var queued = asyncSource.EnumerateDirectoryAsync("/", queuedCancellation.Token);
            queuedCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(10)));

            firstCancellation.Cancel();
            Assert.False(first.IsCompleted);
            Assert.Equal(2, source.Calls);
        }
        finally
        {
            source.Release.TrySetResult();
        }

        await first!.WaitAsync(TimeSpan.FromSeconds(10));
        await second!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task LegacyMetadataErrorsAreObserved()
    {
        var source = new MemoryFilePanelView(new PanelSourceId("legacy"));
        source.FailEnumeratePath = "/";
        await Assert.ThrowsAsync<IOException>(() => source.AsAsync().EnumerateDirectoryAsync("/"));
    }

    private sealed class ControlledLegacySource : MemoryFilePanelView, IFilePanelSource
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ControlledLegacySource() : base(new PanelSourceId("controlled-legacy")) { }

        IReadOnlyList<FilePanelItem> IFilePanelSource.EnumerateDirectory(string path, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                FirstStarted.TrySetResult();
            else
                SecondStarted.TrySetResult();
            // Represents a legacy API that does not support cancellation.
            Release.Task.GetAwaiter().GetResult();
            return base.EnumerateDirectory(path, CancellationToken.None);
        }
    }
}
