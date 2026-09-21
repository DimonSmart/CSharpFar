using Microsoft.Win32.SafeHandles;

namespace CSharpFar.App.Viewer;

internal sealed class RandomAccessFileByteReader : IFileByteReader, IDisposable
{
    private readonly string _filePath;
    private long _lastKnownLength;

    public RandomAccessFileByteReader(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _lastKnownLength = GetLengthCore();
    }

    public string FilePath => _filePath;

    public long Length
    {
        get
        {
            try
            {
                long length = GetLengthCore();
                Volatile.Write(ref _lastKnownLength, length);
                return length;
            }
            catch (Exception ex) when (IsTransientFileAccess(ex))
            {
                return Volatile.Read(ref _lastKnownLength);
            }
        }
    }

    public Task<int> ReadAsync(
        long offset,
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        return ReadCoreAsync(offset, buffer, cancellationToken);
    }

    public void Dispose()
    {
        // The reader is path-based and owns no persistent file handle.
    }

    internal static bool IsTransientFileAccess(Exception exception)
    {
        if (exception is FileNotFoundException or DirectoryNotFoundException)
            return true;

        if (exception is not IOException io)
            return false;

        int errorCode = io.HResult & 0xFFFF;
        return errorCode is 2 or 3 or 32 or 33;
    }

    private async Task<int> ReadCoreAsync(
        long offset,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (buffer.IsEmpty)
            return 0;

        try
        {
            using SafeFileHandle handle = OpenHandle();
            long length = RandomAccess.GetLength(handle);
            Volatile.Write(ref _lastKnownLength, length);
            if (offset >= length)
                return 0;

            int totalRead = 0;
            int requested = (int)Math.Min(buffer.Length, length - offset);
            while (totalRead < requested)
            {
                int read = await RandomAccess
                    .ReadAsync(handle, buffer.Slice(totalRead, requested - totalRead), offset + totalRead, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;

                totalRead += read;
            }

            return totalRead;
        }
        catch (Exception ex) when (IsTransientFileAccess(ex))
        {
            return 0;
        }
    }

    private long GetLengthCore()
    {
        using SafeFileHandle handle = OpenHandle();
        return RandomAccess.GetLength(handle);
    }

    private SafeFileHandle OpenHandle() =>
        File.OpenHandle(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileOptions.RandomAccess);
}
