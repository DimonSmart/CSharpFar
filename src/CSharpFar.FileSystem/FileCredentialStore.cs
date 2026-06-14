using System.Text;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.FileSystem;

public sealed class FileCredentialStore : ICredentialStore
{
    private readonly string _directory;

    public FileCredentialStore(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _directory = Path.Combine(configDirectory, "credentials");
        Directory.CreateDirectory(_directory);
    }

    public void SavePassword(string credentialId, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);
        ArgumentNullException.ThrowIfNull(password);

        string path = CredentialPath(credentialId);
        File.WriteAllText(path, password, Encoding.UTF8);
        TrySetOwnerOnlyPermissions(path);
    }

    public string? TryReadPassword(string credentialId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);
        string path = CredentialPath(credentialId);
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
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

        return Path.Combine(_directory, credentialId + ".txt");
    }

    private static void TrySetOwnerOnlyPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best effort: credentials remain usable even on file systems that reject chmod.
        }
    }
}
