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
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        Action<FakeConsoleDriver>? closeWhenReadStarts = null;
        closeWhenReadStarts = current =>
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Initial viewer read did not start.");

            if (readerFactory.Current.ReadStarted.Task.IsCompletedSuccessfully)
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                return;
            }

            RearmViewerInputPoll(current, closeWhenReadStarts);
        };
        RearmViewerInputPoll(driver, closeWhenReadStarts);

        FileViewerFor(
            new ScreenRenderer(driver),
            localFileMonitorFactory: null,
            localFileByteReaderFactory: readerFactory).Show(path);

        Assert.True(readerFactory.Current.CancellationObserved);
        Assert.False(readerFactory.Current.DisposedBeforeCancellation);
    }

    [Fact]
    public void LocalViewer_FileChangeDuringInitialReadRetriesWithCurrentContent()
    {
        string path = Write("local-initial-change.txt", "OLD", new UTF8Encoding(false));
        var readerFactory = new ChangingInitialLocalFileByteReaderFactory();
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        int stage = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        Action<FakeConsoleDriver>? advance = null;
        advance = current =>
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("LocalViewer_FileChangeDuringInitialReadRetriesWithCurrentContent did not reach its expected state.");
            if (stage == 0 && readerFactory.Current.ReadStarted.Task.IsCompletedSuccessfully)
            {
                File.WriteAllText(path, "NEW-CONTENT", new UTF8Encoding(false));
                readerFactory.Current.CompleteFirstRead("OLD"u8.ToArray());
                stage = 1;
            }
            else if (stage == 1 &&
                current.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 8))
                    .Contains("NEW-CONTENT", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                stage = 2;
                return;
            }

            RearmViewerInputPoll(current, advance);
        };
        driver.BeforeTryReadInput = advance;
        driver.BeforeReadInput = advance;

        FileViewerFor(
            new ScreenRenderer(driver),
            localFileMonitorFactory: null,
            localFileByteReaderFactory: readerFactory).Show(path);

        Assert.True(readerFactory.Current.ReadCount >= 2);
        Assert.Contains("NEW-CONTENT", WrittenText(driver));
    }

    [Fact]
    public void LocalViewer_TailSlowRefreshStaysResponsiveAndReachesCurrentEnd()
    {
        string path = Write("tail-slow-refresh.txt", "first", new UTF8Encoding(false));
        var monitorFactory = new FakeLocalFileMonitorFactory();
        var readerFactory = new GatedRefreshLocalFileByteReaderFactory();
        var driver = new FakeConsoleDriver(width: 80, height: 8);
        int stage = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        Action<FakeConsoleDriver>? advance = null;
        advance = current =>
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("LocalViewer_TailSlowRefreshStaysResponsiveAndReachesCurrentEnd did not reach its expected state.");
            string content = current.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 6));
            string writes = WrittenText(current);
            if (stage == 0 && content.Contains("first", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F, 'f'));
                stage = 1;
            }
            else if (stage == 1 && writes.Contains("WATCH", StringComparison.Ordinal) &&
                     monitorFactory.CreateCount == 1)
            {
                current.EnqueueKey(Key(ConsoleKey.F, 'f'));
                stage = 2;
            }
            else if (stage == 2 && writes.Contains("TAIL", StringComparison.Ordinal))
            {
                readerFactory.Current.GateNextRead = true;
                File.AppendAllText(path, "\nAPPENDED", new UTF8Encoding(false));
                monitorFactory.Current.MarkDirty();
                stage = 3;
            }
            else if (stage == 3 &&
                     readerFactory.Current.PendingReadStarted.Task.IsCompletedSuccessfully)
            {
                readerFactory.Current.ReleasePendingRead();
                stage = 4;
            }
            else if (stage == 4 &&
                     content.Contains("APPENDED", StringComparison.Ordinal) &&
                     writes.Contains("TAIL", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                stage = 5;
                return;
            }

            RearmViewerInputPoll(current, advance);
        };
        driver.BeforeTryReadInput = advance;
        driver.BeforeReadInput = advance;

        FileViewerFor(
            new ScreenRenderer(driver),
            monitorFactory,
            readerFactory).Show(path);

        string finalContent = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 6));
        Assert.Contains("APPENDED", finalContent);
        Assert.Contains("TAIL", WrittenText(driver));
    }

    [Fact]
    public void VirtualViewer_SlowRenderDoesNotBlockClose()
    {
        byte[] data = Enumerable.Repeat((byte)'x', BlockCache.DefaultBlockSize + 4096).ToArray();
        var reader = new GatedAfterFirstReadByteReader(data);
        var driver = new FakeConsoleDriver(width: 80, height: 12);
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        Action<FakeConsoleDriver>? closeWhenReadStarts = null;
        closeWhenReadStarts = current =>
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Viewer render did not request the delayed block.");

            if (reader.PendingReadStarted.Task.IsCompletedSuccessfully)
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                return;
            }

            RearmViewerInputPoll(current, closeWhenReadStarts);
        };
        RearmViewerInputPoll(driver, closeWhenReadStarts);

        FileViewerFor(new ScreenRenderer(driver)).Show("remote:/slow-render.txt", reader);

        Assert.True(reader.PendingReadStarted.Task.IsCompletedSuccessfully);
        Assert.True(reader.CancellationObserved);
    }

    [Fact]
    public void VirtualViewer_RapidNavigationKeepsLatestRequestedPosition()
    {
        string prefix = "START\n" +
            string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i:D2}")) +
            "\n";
        byte[] data = Encoding.UTF8.GetBytes(
            prefix + new string('a', BlockCache.DefaultBlockSize + 4096) + "\nTAIL");
        var reader = new GatedAfterFirstReadByteReader(data);
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        int stage = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        Action<FakeConsoleDriver>? advance = null;
        advance = current =>
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("VirtualViewer_RapidNavigationKeepsLatestRequestedPosition did not reach its expected state.");
            string content = current.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 8));
            if (stage == 0 && content.Contains("START", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.End));
                stage = 1;
            }
            else if (stage == 1 && reader.PendingReadStarted.Task.IsCompletedSuccessfully)
            {
                current.EnqueueKey(Key(ConsoleKey.Home));
                stage = 2;
            }
            else if (stage == 2 && reader.CancellationObserved &&
                     content.Contains("START", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                stage = 3;
                return;
            }

            RearmViewerInputPoll(current, advance);
        };
        driver.BeforeTryReadInput = advance;
        driver.BeforeReadInput = advance;

        FileViewerFor(new ScreenRenderer(driver)).Show("remote:/rapid-navigation.txt", reader);

        string finalContent = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 8));
        Assert.True(reader.CancellationObserved);
        Assert.Contains("START", finalContent);
        Assert.DoesNotContain("TAIL", finalContent);
    }

    [Fact]
    public void VirtualViewer_EscapeCancelsPendingSearchAndKeepsViewerOpen()
    {
        string prefix = "START\n" +
            string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i:D2}")) +
            "\n";
        byte[] data = Encoding.UTF8.GetBytes(
            prefix + new string('a', BlockCache.DefaultBlockSize + 4096) + "\nTARGET");
        var reader = new GatedAfterFirstReadByteReader(data);
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        int stage = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        Action<FakeConsoleDriver>? advance = null;
        advance = current =>
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("VirtualViewer_EscapeCancelsPendingSearchAndKeepsViewerOpen did not reach its expected state.");
            string content = current.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 8));
            string writes = WrittenText(current);
            if (stage == 0 && content.Contains("START", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F7));
                EnqueueText(current, "TARGET");
                current.EnqueueKey(Key(ConsoleKey.Enter));
                stage = 1;
            }
            else if (stage == 1 && reader.PendingReadStarted.Task.IsCompletedSuccessfully)
            {
                current.EnqueueKey(Key(ConsoleKey.Escape));
                stage = 2;
            }
            else if (stage == 2 && reader.CancellationObserved)
            {
                current.EnqueueKey(Key(ConsoleKey.F7));
                EnqueueText(current, "START");
                current.EnqueueKey(Key(ConsoleKey.Enter));
                stage = 3;
            }
            else if (stage == 3 &&
                     writes.Contains("FIND", StringComparison.Ordinal) &&
                     content.Contains("START", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                stage = 4;
                return;
            }

            RearmViewerInputPoll(current, advance);
        };
        driver.BeforeTryReadInput = advance;
        driver.BeforeReadInput = advance;

        FileViewerFor(new ScreenRenderer(driver)).Show("remote:/cancel-search.txt", reader);

        Assert.Equal(4, stage);
        Assert.True(reader.CancellationObserved);
        Assert.Contains("START", WrittenText(driver));
    }

    [Fact]
    public void VirtualViewer_NewSearchCancelsAndSupersedesPendingSearch()
    {
        string prefix = "START\n" +
            string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i:D2}")) +
            "\n";
        byte[] data = Encoding.UTF8.GetBytes(
            prefix + new string('a', BlockCache.DefaultBlockSize + 4096) + "\nOLDTARGET");
        var reader = new GatedAfterFirstReadByteReader(data);
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        int stage = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        Action<FakeConsoleDriver>? advance = null;
        advance = current =>
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("VirtualViewer_NewSearchCancelsAndSupersedesPendingSearch did not reach its expected state.");
            string content = current.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 8));
            string writes = WrittenText(current);
            if (stage == 0 && content.Contains("START", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F7));
                EnqueueText(current, "OLDTARGET");
                current.EnqueueKey(Key(ConsoleKey.Enter));
                stage = 1;
            }
            else if (stage == 1 && reader.PendingReadStarted.Task.IsCompletedSuccessfully)
            {
                current.EnqueueKey(Key(ConsoleKey.F7));
                EnqueueText(current, "START");
                current.EnqueueKey(Key(ConsoleKey.Enter));
                stage = 2;
            }
            else if (stage == 2 && reader.CancellationObserved &&
                     writes.Contains("FIND", StringComparison.Ordinal) &&
                     content.Contains("START", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                stage = 3;
                return;
            }

            RearmViewerInputPoll(current, advance);
        };
        driver.BeforeTryReadInput = advance;
        driver.BeforeReadInput = advance;

        FileViewerFor(new ScreenRenderer(driver)).Show("remote:/superseded-search.txt", reader);

        Assert.True(reader.CancellationObserved);
        Assert.Contains("START", WrittenText(driver));
        Assert.DoesNotContain("OLDTARGET",
            driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 1, 80, 8)));
    }

    [Fact]
    public void VirtualViewer_EncodingChangeCancelsStalePendingRead()
    {
        byte[] data = new byte[BlockCache.DefaultBlockSize + 4096];
        var reader = new OneShotGatedByteReader(data);
        var driver = new FakeConsoleDriver(width: 80, height: 10);
        int stage = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        Action<FakeConsoleDriver>? advance = null;
        advance = current =>
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("VirtualViewer_EncodingChangeCancelsStalePendingRead did not reach its expected state.");
            string header = current.GetRegionText(new CSharpFar.Console.Models.Rect(0, 0, 80, 1));
            if (stage == 0 && header.Contains("HEX", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F7));
                EnqueueText(current, "FF");
                current.EnqueueKey(Key(ConsoleKey.Enter));
                stage = 1;
            }
            else if (stage == 1 && reader.PendingReadStarted.Task.IsCompletedSuccessfully)
            {
                current.EnqueueKey(Key(ConsoleKey.F8));
                stage = 2;
            }
            else if (stage == 2 && reader.CancellationObserved &&
                     header.Contains("TEXT", StringComparison.Ordinal))
            {
                current.EnqueueKey(Key(ConsoleKey.F10));
                stage = 3;
                return;
            }

            RearmViewerInputPoll(current, advance);
        };
        driver.BeforeTryReadInput = advance;
        driver.BeforeReadInput = advance;

        FileViewerFor(new ScreenRenderer(driver)).Show("remote:/encoding-race.bin", reader);

        string finalHeader = driver.GetRegionText(new CSharpFar.Console.Models.Rect(0, 0, 80, 1));
        Assert.True(reader.CancellationObserved);
        Assert.Contains("TEXT", finalHeader);
        Assert.DoesNotContain("HEX", finalHeader);
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
    public async Task ViewerAsyncCoordinator_InvalidationRejectsAlreadyQueuedUiResult()
    {
        using var coordinator = new ViewerAsyncCoordinator();
        coordinator.BeginSession();
        long version = coordinator.StateVersion;
        int? applied = null;

        coordinator.StartLatest(
            "search",
            version,
            _ => Task.FromResult(42),
            value => applied = value);

        for (int i = 0; i < 16 && !coordinator.HasPending; i++)
            await Task.Yield();

        coordinator.InvalidateState();
        coordinator.DrainUiCompletions();

        Assert.Null(applied);
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

            RearmViewerInputPoll(current, closeWhenError);
        };
        driver.BeforeTryReadInput = closeWhenError;
        driver.BeforeReadInput = closeWhenError;

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
        await reader.Disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(stream.IsDisposed);
    }

    private static void RearmViewerInputPoll(
        FakeConsoleDriver driver,
        Action<FakeConsoleDriver>? callback)
    {
        driver.BeforeTryReadInput = callback;
        driver.BeforeReadInput = callback;
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

            RearmViewerInputPoll(current, poll);
        };

        driver.BeforeTryReadInput = poll;

        driver.BeforeReadInput = poll;
    }

    private sealed class GatedRefreshLocalFileByteReaderFactory : ILocalFileByteReaderFactory
    {
        public GatedRefreshLocalFileByteReader Current { get; private set; } = null!;

        public ILocalFileByteReader Create(string filePath)
        {
            Current = new GatedRefreshLocalFileByteReader(filePath);
            return Current;
        }
    }

    private sealed class GatedRefreshLocalFileByteReader(string filePath) : ILocalFileByteReader
    {
        private readonly RandomAccessFileByteReader _inner = new(filePath);
        private readonly object _gate = new();
        private PendingLocalRead? _pending;

        public bool GateNextRead { get; set; }
        public TaskCompletionSource<bool> PendingReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public long Length => _inner.Length;
        public int TransientFailureVersion => _inner.TransientFailureVersion;

        public Task<int> ReadAsync(
            long offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!GateNextRead)
                return _inner.ReadAsync(offset, buffer, cancellationToken);

            GateNextRead = false;
            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));
            lock (_gate)
            {
                _pending = new PendingLocalRead(
                    offset,
                    buffer,
                    cancellationToken,
                    completion,
                    registration);
            }

            PendingReadStarted.TrySetResult(true);
            return completion.Task;
        }

        public void ReleasePendingRead()
        {
            PendingLocalRead pending;
            lock (_gate)
            {
                pending = _pending
                    ?? throw new InvalidOperationException("No delayed local read is pending.");
                _pending = null;
            }

            _ = CompletePendingReadAsync(pending);
        }

        public void UpdateKnownLength(long length) => _inner.UpdateKnownLength(length);

        public void Dispose() => _inner.Dispose();

        private async Task CompletePendingReadAsync(PendingLocalRead pending)
        {
            try
            {
                int read = await _inner.ReadAsync(
                        pending.Offset,
                        pending.Buffer,
                        pending.CancellationToken)
                    .ConfigureAwait(false);
                pending.Completion.TrySetResult(read);
            }
            catch (OperationCanceledException)
            {
                pending.Completion.TrySetCanceled(pending.CancellationToken);
            }
            catch (Exception ex)
            {
                pending.Completion.TrySetException(ex);
            }
            finally
            {
                pending.Registration.Dispose();
            }
        }

        private sealed record PendingLocalRead(
            long Offset,
            Memory<byte> Buffer,
            CancellationToken CancellationToken,
            TaskCompletionSource<int> Completion,
            CancellationTokenRegistration Registration);
    }

    private sealed class ChangingInitialLocalFileByteReaderFactory : ILocalFileByteReaderFactory
    {
        public ChangingInitialLocalFileByteReader Current { get; private set; } = null!;

        public ILocalFileByteReader Create(string filePath)
        {
            Current = new ChangingInitialLocalFileByteReader(filePath);
            return Current;
        }
    }

    private sealed class ChangingInitialLocalFileByteReader(string filePath) : ILocalFileByteReader
    {
        private readonly RandomAccessFileByteReader _inner = new(filePath);
        private readonly object _gate = new();
        private Memory<byte> _firstBuffer;
        private TaskCompletionSource<int>? _firstCompletion;
        private int _readCount;

        public TaskCompletionSource<bool> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public long Length => _inner.Length;
        public int TransientFailureVersion => _inner.TransientFailureVersion;
        public int ReadCount => Volatile.Read(ref _readCount);

        public Task<int> ReadAsync(
            long offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int sequence = Interlocked.Increment(ref _readCount);
            if (sequence != 1)
                return _inner.ReadAsync(offset, buffer, cancellationToken);

            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _firstBuffer = buffer;
                _firstCompletion = completion;
            }

            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            ReadStarted.TrySetResult(true);
            return completion.Task;
        }

        public void CompleteFirstRead(byte[] bytes)
        {
            TaskCompletionSource<int> completion;
            Memory<byte> buffer;
            lock (_gate)
            {
                completion = _firstCompletion
                    ?? throw new InvalidOperationException("The first read has not started.");
                buffer = _firstBuffer;
            }

            int count = Math.Min(bytes.Length, buffer.Length);
            bytes.AsMemory(0, count).CopyTo(buffer);
            completion.TrySetResult(count);
        }

        public void UpdateKnownLength(long length) => _inner.UpdateKnownLength(length);

        public void Dispose() => _inner.Dispose();
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
        private int _disposedBeforeCancellation;

        public long Length => Volatile.Read(ref _length);
        public int TransientFailureVersion => 0;
        public TaskCompletionSource<bool> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved => Volatile.Read(ref _cancellationObserved) != 0;
        public bool DisposedBeforeCancellation => Volatile.Read(ref _disposedBeforeCancellation) != 0;

        public Task<int> ReadAsync(
            long offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _ = offset;
            _ = buffer;
            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ReadStarted.TrySetResult(true);
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
            if (!CancellationObserved)
                Volatile.Write(ref _disposedBeforeCancellation, 1);
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

    private sealed class OneShotGatedByteReader(byte[] data) : IFileByteReader
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
            if (call != 2)
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
