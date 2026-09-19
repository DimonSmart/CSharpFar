using Microsoft.Win32.SafeHandles;

namespace CSharpFar.App.Viewer;

internal sealed class RandomAccessFileByteReader : IFileByteReader, IDisposable
{
    private readonly string _filePath;
    private long _lastKnownLength;

    public RandomAccessFileByteReader(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        using SafeFileHandle handle = OpenHandle();
        _lastKnownLength = RandomAccess.GetLength(handle);
    }

    internal string FilePath => _filePath;

    public long Length
    {
        get
        {
            try
            {
                using SafeFileHandle handle = OpenHandle();
                long length = RandomAccess.GetLength(handle);
                Volatile.Write(ref _lastKnownLength, length);
                return length;
            }
            catch (Exception ex) when (IsTransientFileAccess(ex))
            {
                return Math.Max(0, Volatile.Read(ref _lastKnownLength));
            }
        }
    }

    public async Task<int> ReadAsync(
        long offset,
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        using SafeFileHandle handle = OpenHandle();
        int read = await RandomAccess
            .ReadAsync(handle, buffer, offset, cancellationToken)
            .ConfigureAwait(false);

        Volatile.Write(ref _lastKnownLength, RandomAccess.GetLength(handle));
        return read;
    }

    public void Dispose()
    {
    }

    internal static bool IsTransientFileAccess(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private SafeFileHandle OpenHandle() =>
        File.OpenHandle(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileOptions.RandomAccess);
}
