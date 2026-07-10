using System.Security.Cryptography;

namespace CSharpFar.Core.Comparison;

public sealed class FileContentHasher
{
    private const int BufferSize = 1024 * 128;
    private readonly IComparisonFileSystem _fileSystem;

    public FileContentHasher(IComparisonFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem ?? new LocalComparisonFileSystem();
    }

    public string ComputeSha256(FileEntry entry, CancellationToken cancellationToken = default)
    {
        using var sha = SHA256.Create();
        byte[] buffer = new byte[BufferSize];
        using Stream stream = _fileSystem.OpenRead(entry.FullPath);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash ?? []);
    }
}
