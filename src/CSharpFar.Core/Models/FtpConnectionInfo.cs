namespace CSharpFar.Core.Models;

public enum FtpConnectionSecurityMode
{
    PlainFtp,
    ExplicitFtps,
    ImplicitFtps,
    Auto,
}

public enum FtpDataConnectionMode
{
    AutoPassive,
    Passive,
    Active,
}

public sealed record FtpConnectionInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Host { get; init; }
    public int Port { get; init; } = 21;
    public required string Username { get; init; }
    public string RemoteRootPath { get; init; } = "/";
    public string? CredentialId { get; init; }
    public FtpConnectionSecurityMode SecurityMode { get; init; } = FtpConnectionSecurityMode.ExplicitFtps;
    public FtpDataConnectionMode DataConnectionMode { get; init; } = FtpDataConnectionMode.AutoPassive;
    public bool UseDataConnectionTls { get; init; } = true;
    public string? ExpectedTlsCertificateFingerprint { get; init; }
    public int? ActiveModeLocalPortFrom { get; init; }
    public int? ActiveModeLocalPortTo { get; init; }
    public bool ShowInDriveSelection { get; init; } = true;
}
