namespace CSharpFar.App.Viewer;

internal interface IFileByteReader
{
    long Length { get; }

    Task<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default);
}
