namespace CSharpFar.Core.Abstractions;

public interface ICredentialStore
{
    void SavePassword(string credentialId, string password);
    string? TryReadPassword(string credentialId);
    void DeletePassword(string credentialId);
}
