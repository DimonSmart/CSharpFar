using System.Text;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Text;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies TextFileReader encoding detection and F3 viewer behavior.
/// </summary>
public partial class FileViewerTests
{
    [Fact]
    public void RandomAccessFileByteReader_UnexpectedExceptionsAreNotTransient()
    {
        Assert.False(RandomAccessFileByteReader.IsTransientFileAccess(
            new ArgumentException("programming error")));
        Assert.False(RandomAccessFileByteReader.IsTransientFileAccess(
            new InvalidOperationException("invalid state")));
    }

    [Fact]
    public void RandomAccessFileByteReader_DoesNotHoldPersistentHandle()
    {
        string path = Write("non-blocking-reader.txt", "content", new UTF8Encoding(false));
        using var reader = new RandomAccessFileByteReader(path);

        using var exclusive = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        Assert.True(exclusive.CanRead);
    }

    [Fact]
    public async Task RandomAccessFileByteReader_ReadsReplacementAtSamePath()
    {
        string path = WritePath("replace-reader.txt");
        File.WriteAllBytes(path, "AAAA"u8.ToArray());
        using var reader = new RandomAccessFileByteReader(path);
        var buffer = new byte[4];

        Assert.Equal(4, await reader.ReadAsync(0, buffer));
        Assert.Equal("AAAA", Encoding.ASCII.GetString(buffer));

        string replacement = WritePath("replace-reader.tmp");
        File.WriteAllBytes(replacement, "BBBB"u8.ToArray());
        File.Move(replacement, path, overwrite: true);

        Array.Clear(buffer);
        Assert.Equal(4, await reader.ReadAsync(0, buffer));
        Assert.Equal("BBBB", Encoding.ASCII.GetString(buffer));
    }

    [Fact]
    public async Task BlockCache_ClearDropsSameLengthStaleBlock()
    {
        string path = WritePath("cache-overwrite.txt");
        File.WriteAllBytes(path, "AAAA"u8.ToArray());
        using var reader = new RandomAccessFileByteReader(path);
        var cache = new BlockCache(reader, blockSize: 4, capacity: 2);

        Assert.Equal("AAAA", Encoding.ASCII.GetString((await cache.ReadBlockAsync(0)).Span));

        File.WriteAllBytes(path, "BBBB"u8.ToArray());
        Assert.Equal("AAAA", Encoding.ASCII.GetString((await cache.ReadBlockAsync(0)).Span));

        cache.Clear();
        Assert.Equal("BBBB", Encoding.ASCII.GetString((await cache.ReadBlockAsync(0)).Span));
    }

    [Fact]
    public void LocalFileMonitor_DirtySignalForcesRefreshWithUnchangedMetadata()
    {
        var snapshot = new LocalFileSnapshot(
            Exists: true,
            Length: 4,
            LastWriteTimeUtc: new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));

        Assert.False(LocalFileMonitor.ShouldRefresh(snapshot, snapshot, watcherDirty: false));
        Assert.True(LocalFileMonitor.ShouldRefresh(snapshot, snapshot, watcherDirty: true));
        Assert.True(LocalFileMonitor.ShouldRefresh(
            snapshot,
            snapshot with { Length = 5 },
            watcherDirty: false));
    }

    [Fact]
    public void LocalFileMonitoringSession_IsLazyAndReusesActiveMonitor()
    {
        string path = Write("monitor-lifecycle.txt", "AAAA", new UTF8Encoding(false));
        Assert.True(LocalFileMonitor.TryCaptureSnapshot(path, out LocalFileSnapshot snapshot));
        var factory = new FakeLocalFileMonitorFactory();

        using var monitoring = new LocalFileMonitoringSession(path, snapshot, factory);

        Assert.False(monitoring.IsActive);
        Assert.False(monitoring.PendingRefresh);
        Assert.Equal(0, factory.CreateCount);

        monitoring.Activate();

        Assert.True(monitoring.IsActive);
        Assert.True(monitoring.PendingRefresh);
        Assert.Equal(1, factory.CreateCount);

        long initialRefresh = monitoring.BeginRefresh();
        monitoring.Commit(snapshot, initialRefresh);
        Assert.False(monitoring.PendingRefresh);

        monitoring.Activate();
        Assert.Equal(1, factory.CreateCount);

        FakeLocalFileMonitor first = factory.Current;
        monitoring.Deactivate();

        Assert.True(first.IsDisposed);
        Assert.False(monitoring.IsActive);
        Assert.False(monitoring.PendingRefresh);

        monitoring.Activate();

        Assert.Equal(2, factory.CreateCount);
        Assert.True(monitoring.PendingRefresh);
        Assert.NotSame(first, factory.Current);
    }

    [Fact]
    public void LocalFileMonitoringSession_NotificationDuringRefreshRemainsPending()
    {
        string path = Write("monitor-race.txt", "AAAA", new UTF8Encoding(false));
        Assert.True(LocalFileMonitor.TryCaptureSnapshot(path, out LocalFileSnapshot snapshot));
        var factory = new FakeLocalFileMonitorFactory();

        using var monitoring = new LocalFileMonitoringSession(path, snapshot, factory);
        monitoring.Activate();
        monitoring.Commit(snapshot, monitoring.BeginRefresh());

        factory.Current.MarkDirty();
        monitoring.DetectChanges();
        Assert.True(monitoring.PendingRefresh);

        long refreshVersion = monitoring.BeginRefresh();
        factory.Current.MarkDirty();
        monitoring.Commit(snapshot, refreshVersion);

        Assert.True(monitoring.PendingRefresh);

        long secondRefreshVersion = monitoring.BeginRefresh();
        monitoring.Commit(snapshot, secondRefreshVersion);

        Assert.False(monitoring.PendingRefresh);
    }

    [Fact]
    public void LocalFileMonitoringSession_PollingWorksWithoutNotifications()
    {
        string path = Write("monitor-polling-only.txt", "AAAA", new UTF8Encoding(false));
        Assert.True(LocalFileMonitor.TryCaptureSnapshot(path, out LocalFileSnapshot snapshot));
        var factory = new FakeLocalFileMonitorFactory(notificationsAvailable: false);

        using var monitoring = new LocalFileMonitoringSession(path, snapshot, factory);
        monitoring.Activate();
        monitoring.Commit(snapshot, monitoring.BeginRefresh());

        File.AppendAllText(path, "B", new UTF8Encoding(false));
        monitoring.DetectChanges();

        Assert.True(monitoring.IsActive);
        Assert.False(monitoring.NotificationsAvailable);
        Assert.True(monitoring.PendingRefresh);
        Assert.Equal(1, factory.CreateCount);

        monitoring.DetectChanges();
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void LocalFileMonitoringSession_DeleteRecreateKeepsRefreshPending()
    {
        string path = Write("monitor-delete-recreate.txt", "old", new UTF8Encoding(false));
        Assert.True(LocalFileMonitor.TryCaptureSnapshot(path, out LocalFileSnapshot snapshot));
        var factory = new FakeLocalFileMonitorFactory();

        using var monitoring = new LocalFileMonitoringSession(path, snapshot, factory);
        monitoring.Activate();
        monitoring.Commit(snapshot, monitoring.BeginRefresh());

        File.Delete(path);
        factory.Current.MarkDirty();
        monitoring.DetectChanges();

        Assert.True(monitoring.PendingRefresh);

        File.WriteAllText(path, "new", new UTF8Encoding(false));
        monitoring.DetectChanges();

        Assert.True(monitoring.PendingRefresh);
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void LocalFileMonitoringSession_NotificationBackendErrorRequestsResync()
    {
        string path = Write("monitor-error.txt", "AAAA", new UTF8Encoding(false));
        Assert.True(LocalFileMonitor.TryCaptureSnapshot(path, out LocalFileSnapshot snapshot));
        var factory = new FakeLocalFileMonitorFactory();

        using var monitoring = new LocalFileMonitoringSession(path, snapshot, factory);
        monitoring.Activate();
        monitoring.Commit(snapshot, monitoring.BeginRefresh());

        factory.Current.FailNotifications();
        monitoring.DetectChanges();

        Assert.True(monitoring.IsActive);
        Assert.False(monitoring.NotificationsAvailable);
        Assert.True(monitoring.PendingRefresh);
        Assert.Equal(1, factory.CreateCount);
    }


    [Fact]
    public async Task BlockCache_ReassemblesShortReadsAcrossBlocksAndEof()
    {
        var reader = new ShortReadByteReader(Enumerable.Range(0, 53).Select(i => (byte)i).ToArray());
        var cache = new BlockCache(reader, blockSize: 16, capacity: 2);
        byte[] bytes = new byte[64];

        Assert.Equal(16, (await cache.ReadBlockAsync(0)).Length);
        Assert.Equal(53, await cache.ReadAsync(0, bytes));
        Assert.Equal(Enumerable.Range(0, 53).Select(i => (byte)i), bytes.Take(53));
        Assert.Equal(5, (await cache.ReadBlockAsync(48)).Length);
        Assert.Empty((await cache.ReadBlockAsync(64)).ToArray());
        Assert.Equal(0, await cache.ReadAsync(53, bytes));
        Assert.True(reader.ReadCount > 4);
    }

    [Fact]
    public async Task BlockCache_RechecksFileLengthAndDoesNotCacheIncompleteInteriorReads()
    {
        var reader = new ShortReadByteReader("1234567890ABCDEF"u8.ToArray());
        var cache = new BlockCache(reader, blockSize: 8, capacity: 2);
        Assert.Equal("12345678", Encoding.ASCII.GetString((await cache.ReadBlockAsync(0)).Span));

        reader.Resize(3);
        Assert.Equal("123", Encoding.ASCII.GetString((await cache.ReadBlockAsync(0)).Span));
        reader.Resize(12);
        Assert.Equal(8, (await cache.ReadBlockAsync(0)).Length);

        reader.ZeroReadOnce = true;
        cache.Clear();
        Assert.Empty((await cache.ReadBlockAsync(0)).ToArray());
        Assert.Equal(8, (await cache.ReadBlockAsync(0)).Length);
    }

    [Fact]
    public async Task BlockCache_ShortReadHonorsCancellation()
    {
        var reader = new ShortReadByteReader(new byte[64]);
        var cache = new BlockCache(reader, blockSize: 16);
        using var cancellation = new CancellationTokenSource();
        reader.AfterRead = () => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.ReadBlockAsync(0, cancellation.Token));
    }

    [Fact]
    public async Task ShortReads_MatchMemoryReaderForTextLinesAndSearch()
    {
        byte[] content = Encoding.UTF8.GetBytes("first line\nsecond needle line\nthird line\n");
        var shortReader = new ShortReadByteReader(content);
        var normalReader = new MemoryFileByteReader(content);
        var shortCache = new BlockCache(shortReader, blockSize: 8, capacity: 2);
        var normalCache = new BlockCache(normalReader, blockSize: 8, capacity: 2);
        var shortScanner = await LineScanner.CreateAsync(shortCache, shortReader);
        var normalScanner = await LineScanner.CreateAsync(normalCache, normalReader);

        ScannedLines actual = await shortScanner.ReadLinesAsync(0, 3, 256);
        ScannedLines expected = await normalScanner.ReadLinesAsync(0, 3, 256);
        Assert.Equal(expected.Lines, actual.Lines);
        Assert.Equal(expected.NextOffset, actual.NextOffset);

        var request = new ViewerSearchRequest("needle", CaseSensitive: true,
            WholeWords: false, UseRegex: false, SearchHex: false);
        var shortMatch = await ViewerSearchEngine.FindAsync(shortReader,
            new LargeFileViewerState(shortCache, shortScanner), request, searchBackward: false);
        var normalMatch = await ViewerSearchEngine.FindAsync(normalReader,
            new LargeFileViewerState(normalCache, normalScanner), request, searchBackward: false);
        Assert.Equal(normalMatch, shortMatch);
        Assert.NotNull(shortMatch);
    }

    [Fact]
    public async Task ShortReads_HexSearchFindsNeedleAcrossBlockBoundaries()
    {
        byte[] bytes = [0, 1, 2, 3, 4, 5, 0xDE, 0xAD, 0xBE, 0xEF, 10, 11, 12];
        var shortReader = new ShortReadByteReader(bytes);
        var cache = new BlockCache(shortReader, blockSize: 8, capacity: 2);
        var scanner = await LineScanner.CreateAsync(cache, shortReader);
        var state = new LargeFileViewerState(cache, scanner);
        var request = new ViewerSearchRequest("DE AD BE EF", CaseSensitive: true,
            WholeWords: false, UseRegex: false, SearchHex: true);

        var found = await ViewerSearchEngine.FindAsync(shortReader, state, request, searchBackward: false);
        Assert.NotNull(found);
        Assert.Equal(6, found.ByteOffset);
        Assert.True(found.IsHex);
        byte[] output = new byte[8];
        Assert.Equal(7, await cache.ReadAsync(6, output));
        Assert.Equal(bytes.Skip(6), output.Take(7));
    }


    [Fact]
    public void VirtualViewer_EscapeCancelsPendingInitialRead()
    {
        var reader = new PendingInitialReadReader();
        var driver = new CSharpFar.Testing.FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        FileViewerFor(new ScreenRenderer(driver)).Show("remote:/slow.txt", reader);

        Assert.True(reader.CancellationObserved);
    }

    private sealed class PendingInitialReadReader : IFileByteReader
    {
        public long Length => 32;
        public bool CancellationObserved { get; private set; }

        public Task<int> ReadAsync(long offset, Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() =>
            {
                CancellationObserved = true;
                completion.TrySetCanceled(cancellationToken);
            });
            return completion.Task;
        }
    }
    private sealed class ShortReadByteReader(byte[] data) : IFileByteReader
    {
        private byte[] _data = data;
        public int ReadCount { get; private set; }
        public bool ZeroReadOnce { get; set; }
        public Action? AfterRead { get; set; }
        public long Length => _data.Length;

        public void Resize(int length) => Array.Resize(ref _data, length);

        public Task<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (ZeroReadOnce)
            {
                ZeroReadOnce = false;
                return Task.FromResult(0);
            }
            int count = (int)Math.Min(Math.Min(buffer.Length, 1 + ReadCount % 7),
                Math.Max(0, Length - offset));
            _data.AsMemory((int)Math.Min(offset, Length), count).CopyTo(buffer);
            AfterRead?.Invoke();
            return Task.FromResult(count);
        }
    }

}
