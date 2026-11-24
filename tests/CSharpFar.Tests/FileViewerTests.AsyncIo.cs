using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Testing;

namespace CSharpFar.Tests;

public partial class FileViewerTests
{
    [Fact]
    public void VirtualViewer_SlowRenderDoesNotBlockClose()
    {
        byte[] data = Enumerable.Repeat((byte)'x', BlockCache.DefaultBlockSize + 4096).ToArray();
        var reader = new GatedAfterFirstReadByteReader(data);
        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.BeforeTryReadInput = _ =>
        {
            if (!reader.PendingReadStarted.Task.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Viewer render did not request the delayed block.");
        };
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(new ScreenRenderer(driver)).Show("remote:/slow-render.txt", reader);

        Assert.True(reader.PendingReadStarted.Task.IsCompletedSuccessfully);
        Assert.True(reader.CancellationObserved);
    }

    [Fact]
    public async Task ViewerSearchEngine_CancellationInterruptsPendingRead()
    {
        byte[] data = Enumerable.Repeat((byte)'a', BlockCache.DefaultBlockSize + 4096).ToArray();
        var reader = new GatedAfterFirstReadByteReader(data);
        var cache = new BlockCache(reader);
        var scanner = await LineScanner.CreateAsync(cache, reader);
        var state = new LargeFileViewerState(cache, scanner);
        using var cancellation = new CancellationTokenSource();

        Task<ViewerSearchMatch?> search = ViewerSearchEngine.FindAsync(
            reader,
            state,
            new ViewerSearchRequest(
                "not-present",
                CaseSensitive: true,
                WholeWords: false,
                UseRegex: false,
                SearchHex: false),
            searchBackward: false,
            cancellation.Token);

        await reader.PendingReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search);
        Assert.True(reader.CancellationObserved);
    }

    [Fact]
    public async Task ViewerAsyncCoordinator_LatestRequestWins()
    {
        using var coordinator = new ViewerAsyncCoordinator();
        coordinator.BeginSession();
        long version = coordinator.StateVersion;
        var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<int>();

        coordinator.StartLatest(
            "search",
            version,
            async _ =>
            {
                try { return await first.Task.ConfigureAwait(false); }
                finally { firstFinished.TrySetResult(true); }
            },
            applied.Add);

        coordinator.StartLatest(
            "search",
            version,
            async _ =>
            {
                try { return await second.Task.ConfigureAwait(false); }
                finally { secondFinished.TrySetResult(true); }
            },
            applied.Add);

        first.TrySetResult(1);
        second.TrySetResult(2);
        await Task.WhenAll(firstFinished.Task, secondFinished.Task).WaitAsync(TimeSpan.FromSeconds(5));

        for (int i = 0; i < 8 && !coordinator.DrainUiCompletions(); i++)
            await Task.Yield();

        Assert.Equal([2], applied);
    }

    [Fact]
    public async Task BlockCache_ClearRejectsStaleInFlightFill()
    {
        var reader = new SequencedCompletionByteReader(length: 4);
        var cache = new BlockCache(reader, blockSize: 4, capacity: 2);

        Task<ReadOnlyMemory<byte>> staleRead = cache.ReadBlockAsync(0);
        await reader.WaitForRequestAsync(1);
        cache.Clear();
        reader.Complete(1, "AAAA"u8.ToArray());
        Assert.Equal("AAAA"u8.ToArray(), (await staleRead).ToArray());

        Task<ReadOnlyMemory<byte>> freshRead = cache.ReadBlockAsync(0);
        await reader.WaitForRequestAsync(2);
        reader.Complete(2, "BBBB"u8.ToArray());

        Assert.Equal("BBBB"u8.ToArray(), (await freshRead).ToArray());
        Assert.Equal("BBBB"u8.ToArray(), (await cache.ReadBlockAsync(0)).ToArray());
        Assert.Equal(2, reader.ReadCount);
    }

    [Fact]
    public async Task ViewerAsyncCoordinator_StateInvalidationRejectsOldResult()
    {
        using var coordinator = new ViewerAsyncCoordinator();
        coordinator.BeginSession();
        long version = coordinator.StateVersion;
        var operation = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int? applied = null;

        coordinator.StartLatest(
            "search",
            version,
            async _ =>
            {
                try { return await operation.Task.ConfigureAwait(false); }
                finally { finished.TrySetResult(true); }
            },
            value => applied = value);

        coordinator.InvalidateState();
        operation.TrySetResult(42);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 4; i++)
            await Task.Yield();

        coordinator.DrainUiCompletions();
        Assert.Null(applied);
    }

    private sealed class GatedAfterFirstReadByteReader(byte[] data) : IFileByteReader
    {
        private int _readCount;
        private int _cancellationObserved;

        public long Length => data.LongLength;

        public TaskCompletionSource<bool> PendingReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved => Volatile.Read(ref _cancellationObserved) != 0;

        public Task<int> ReadAsync(
            long offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int call = Interlocked.Increment(ref _readCount);
            int count = (int)Math.Min(buffer.Length, Math.Max(0, Length - offset));
            if (call == 1)
            {
                data.AsMemory((int)offset, count).CopyTo(buffer);
                return Task.FromResult(count);
            }

            PendingReadStarted.TrySetResult(true);
            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                Volatile.Write(ref _cancellationObserved, 1);
                completion.TrySetCanceled(cancellationToken);
            });
            return AwaitPendingAsync(completion.Task, registration);
        }

        private static async Task<int> AwaitPendingAsync(
            Task<int> pending,
            CancellationTokenRegistration registration)
        {
            try
            {
                return await pending.ConfigureAwait(false);
            }
            finally
            {
                registration.Dispose();
            }
        }
    }

    private sealed class SequencedCompletionByteReader(long length) : IFileByteReader
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, PendingRead> _pending = [];
        private readonly Dictionary<int, TaskCompletionSource<bool>> _started = [];
        private int _readCount;

        public long Length { get; } = length;
        public int ReadCount => Volatile.Read(ref _readCount);

        public Task<int> ReadAsync(
            long offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sequence = Interlocked.Increment(ref _readCount);
            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> started;
            lock (_gate)
            {
                _pending[sequence] = new PendingRead(buffer, completion);
                if (!_started.TryGetValue(sequence, out started!))
                {
                    started = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _started[sequence] = started;
                }
            }

            started.TrySetResult(true);
            return completion.Task;
        }

        public Task WaitForRequestAsync(int sequence)
        {
            lock (_gate)
            {
                if (!_started.TryGetValue(sequence, out TaskCompletionSource<bool>? started))
                {
                    started = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _started[sequence] = started;
                }

                return started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        public void Complete(int sequence, byte[] bytes)
        {
            PendingRead pending;
            lock (_gate)
            {
                pending = _pending[sequence];
                _pending.Remove(sequence);
            }

            int count = Math.Min(bytes.Length, pending.Buffer.Length);
            bytes.AsMemory(0, count).CopyTo(pending.Buffer);
            pending.Completion.TrySetResult(count);
        }

        private sealed record PendingRead(
            Memory<byte> Buffer,
            TaskCompletionSource<int> Completion);
    }
}
