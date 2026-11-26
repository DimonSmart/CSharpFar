namespace CSharpFar.Plugin.AzureBlob;

// The host may encounter Azure errors on any read/write/flush/close, not just open.
internal sealed class AzureSafeStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => Guard(() => inner.Length);
    public override long Position { get => Guard(() => inner.Position); set => Guard(() => { inner.Position = value; }); }
    public override void Flush() => Guard(inner.Flush);
    public override Task FlushAsync(CancellationToken token) => GuardAsync(() => inner.FlushAsync(token));
    public override int Read(byte[] buffer, int offset, int count) => Guard(() => inner.Read(buffer, offset, count));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    { try { return await inner.ReadAsync(buffer, token).ConfigureAwait(false); } catch (Exception error) { throw AzureErrors.Safe(error); } }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override long Seek(long offset, SeekOrigin origin) => Guard(() => inner.Seek(offset, origin));
    public override void SetLength(long value) => Guard(() => inner.SetLength(value));
    public override void Write(byte[] buffer, int offset, int count) => Guard(() => inner.Write(buffer, offset, count));
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    { try { await inner.WriteAsync(buffer, token).ConfigureAwait(false); } catch (Exception error) { throw AzureErrors.Safe(error); } }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
    protected override void Dispose(bool disposing) { if (disposing) Guard(inner.Dispose); base.Dispose(disposing); }
    public override async ValueTask DisposeAsync()
    { try { await inner.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { throw AzureErrors.Safe(error); } finally { GC.SuppressFinalize(this); } }
    private static T Guard<T>(Func<T> action) { try { return action(); } catch (Exception error) { throw AzureErrors.Safe(error); } }
    private static void Guard(Action action) { try { action(); } catch (Exception error) { throw AzureErrors.Safe(error); } }
    private static async Task GuardAsync(Func<Task> action)
    { try { await action().ConfigureAwait(false); } catch (Exception error) { throw AzureErrors.Safe(error); } }
}
