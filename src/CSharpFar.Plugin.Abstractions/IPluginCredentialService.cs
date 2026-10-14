namespace CSharpFar.Plugin.Abstractions;

/// <summary>Credential identifiers are local to the owning plugin.</summary>
public interface IPluginCredentialService
{
    string? TryReadPassword(string credentialId);
    void SavePassword(string credentialId, string password);
    void DeletePassword(string credentialId);
}
