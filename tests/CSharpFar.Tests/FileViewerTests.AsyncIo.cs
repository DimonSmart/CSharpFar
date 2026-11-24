using System.Text;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Testing;

namespace CSharpFar.Tests;

public partial class FileViewerTests
{
    [Fact]
    public void LocalViewer_CloseCancelsPendingInitialRead()
    {
        string path = Write("local-pending-initial.txt", "content", new UTF8Encoding(false));
        var readerFactory = new PendingLocalFileByteReaderFactory();
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        FileViewerFor(
            new ScreenRenderer(driver),
            localFileMonitorFactory: null,
            localFileByteReaderFactory: readerFactory).Show(path);

        Assert.True(readerFactory.Current.CancellationObserved);
    }

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

    [Fact]
    public void VirtualViewer_ReadErrorIsControlledAndViewerRemainsClosable()
    {
        byte[] data = Enumerable.Repeat((byte)'x', BlockCache.DefaultBlockSize + 4096).ToArray();
        var reader = new FailingAfterFirstReadByteReader(data);
        var driver = new FakeConsoleDriver(width: 80, height: 12);
        int attempts = 0;
        Action<FakeConsoleDriver>? closeWhenError = null;
        closeWhenError = current =>
        {
            if (current.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 10))
                .Contains("Read error:", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                return;
            }

            if (++attempts > 200)
                throw new TimeoutException("Viewer did not surface the controlled read error.");

            current.BeforeTryReadInput = closeWhenError;
        };
        driver.BeforeTryReadInput = closeWhenError;

        FileViewerFor(new ScreenRenderer(driver)).Show("remote:/read-error.txt", reader);

        Assert.Contains("Read error:", WrittenText(driver));
        Assert.True(reader.FailureObserved);
    }

    [Fact]
    public async Task SeekableStreamReader_DisposeCancelsReadAndDefersStreamDispose()
    {
        var stream = new PendingSeekableStream(length: 32);
        var reader = new SeekableStreamFileByteReader(stream);
        byte[] buffer = new byte[8];

        Task<int> read = reader.ReadAsync(0, buffer);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        reader.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.True(stream.IsDisposed);
    }

    private static void CloseWhen(
        FakeConsoleDriver driver,
        Func<FakeConsoleDriver, bool> condition,
        int maxAttempts = 240)
    {
        int attempts = 0;
        Action<FakeConsoleDriver>? poll = null;
        poll = current =>
        {
            if (condition(current))
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                return;
            }

            if (++attempts > maxAttempts)
                throw new TimeoutException("Viewer did not reach the expected state.");

            current.BeforeTryReadInput = poll;
        };

        driver.BeforeTryReadInput = poll;
    }

    private sealed class PendingLocalFileByteReaderFactory : ILocalFileByteReaderFactory
    {
        public PendingLocalFileByteReader Current { get; private set; } = null!;

        public ILocalFileByteReader Create(string filePath)
        {
            _ = filePath;
            Current = new PendingLocalFileByteReader();
            return Current;
        }
    }

    private sealed class PendingLocalFileByteReader : ILocalFileByteReader
    {
        private long _length;
        private int _cancellationObserved;

        public long Length => Volatile.Read(ref _length);
        public int TransientFailureVersion => 0;
        public bool CancellationObserved => Volatile.Read(ref _cancellationObserved) != 0;

        public Task<int> ReadAsync(
            long offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _ = offset;
            _ = buffer;
            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() =>
            {
                Volatile.Write(ref _cancellationObserved, 1);
                completion.TrySetCanceled(cancellationToken);
            });
            return completion.Task;
        }

        public void UpdateKnownLength(long length) =>
            Volatile.Write(ref _length, length);

        public void Dispose()
        {
        }
    }

    private sealed class FailingAfterFirstReadByteReader(byte[] data) : IFileByteReader
    {
        private int _readCount;

        public long Length => data.LongLength;
        public bool FailureObserved { get; private set; }

        public Task<int> ReadAsync(
            long offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int call = Interlocked.Increment(ref _readCount);
            if (call == 1)
            {
                int count = (int)Math.Min(buffer.Length, Math.Max(0, Length - offset));
                data.AsMemory((int)offset, count).CopyTo(buffer);
                return Task.FromResult(count);
            }

            FailureObserved = true;
            return Task.FromException<int>(new IOException("controlled read failure"));
        }
    }

    private sealed class PendingSeekableStream(long length) : Stream
    {
        private long _position;

        public TaskCompletionSource<bool> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsDisposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            return _position;
        }

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult(true);
            return new ValueTask<int>(WaitForCancellationAsync(cancellationToken));
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        private static async Task<int> WaitForCancellationAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
            return 0;
        }
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
