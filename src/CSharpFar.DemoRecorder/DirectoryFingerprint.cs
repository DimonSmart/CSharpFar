using System.Security.Cryptography;

namespace CSharpFar.DemoRecorder;

internal sealed class DirectoryFingerprint
{
    private readonly IReadOnlyDictionary<string, string> _entries;

    private DirectoryFingerprint(IReadOnlyDictionary<string, string> entries)
    {
        _entries = entries;
    }

    public static DirectoryFingerprint Capture(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Fixture directory does not exist: {rootPath}");

        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
            entries[relative] = HashFile(file);
        }

        return new DirectoryFingerprint(entries);
    }

    public void AssertEqual(DirectoryFingerprint other, string rootPath)
    {
        if (_entries.Count != other._entries.Count)
            throw new InvalidOperationException($"Fixture file count changed under {rootPath}.");

        foreach ((string relativePath, string hash) in _entries)
        {
            if (!other._entries.TryGetValue(relativePath, out string? otherHash))
                throw new InvalidOperationException($"Fixture file disappeared during recording: {relativePath}");

            if (!string.Equals(hash, otherHash, StringComparison.Ordinal))
                throw new InvalidOperationException($"Fixture file changed during recording: {relativePath}");
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
