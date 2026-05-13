namespace CSharpFar.Core.Models;

public sealed record SftpConnectionInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Host { get; init; }
    public int Port { get; init; } = 22;
    public required string Username { get; init; }
    public string RemoteRootPath { get; init; } = "/";
    public string? CredentialId { get; init; }
    public string? ExpectedHostKeyFingerprint { get; init; }
    public bool ShowInDriveSelection { get; init; } = true;
}
