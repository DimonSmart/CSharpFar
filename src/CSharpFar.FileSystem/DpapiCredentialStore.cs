using System.Security.Cryptography;
using System.Text;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.FileSystem;

public sealed class DpapiCredentialStore : ICredentialStore
{
    private static readonly byte[] Entropy = "CSharpFar.SftpCredential.v1"u8.ToArray();
    private readonly string _directory;

    public DpapiCredentialStore(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _directory = Path.Combine(configDirectory, "credentials");
        Directory.CreateDirectory(_directory);
    }

    public void SavePassword(string credentialId, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);
        ArgumentNullException.ThrowIfNull(password);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI credential storage is only supported on Windows.");

        byte[] plain = Encoding.UTF8.GetBytes(password);
        byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(CredentialPath(credentialId), encrypted);
    }

    public string? TryReadPassword(string credentialId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);
        string path = CredentialPath(credentialId);
        if (!File.Exists(path))
            return null;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI credential storage is only supported on Windows.");

        byte[] encrypted = File.ReadAllBytes(path);
        byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }

    public void DeletePassword(string credentialId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);
        string path = CredentialPath(credentialId);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string CredentialPath(string credentialId)
    {
        foreach (char ch in Path.GetInvalidFileNameChars())
            credentialId = credentialId.Replace(ch, '_');

        return Path.Combine(_directory, credentialId + ".bin");
    }
}
