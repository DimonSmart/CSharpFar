using Microsoft.Win32.SafeHandles;

namespace CSharpFar.App.Viewer;

internal sealed class RandomAccessFileByteReader : IFileByteReader, IDisposable
{
    private readonly SafeFileHandle _handle;

    public RandomAccessFileByteReader(string filePath)
    {
        _handle = File.OpenHandle(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileOptions.RandomAccess);
    }

    public long Length => RandomAccess.GetLength(_handle);

    public Task<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        RandomAccess.ReadAsync(_handle, buffer, offset, cancellationToken).AsTask();

    public void Dispose() => _handle.Dispose();
}
