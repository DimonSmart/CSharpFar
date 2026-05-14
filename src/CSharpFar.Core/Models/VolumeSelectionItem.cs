namespace CSharpFar.Core.Models;

public enum VolumeSelectionAction
{
    OpenVolume,
    OpenSftp,
    OpenSavedSftp,
    OpenFtp,
    OpenSavedFtp,
}

public sealed class VolumeSelectionItem
{
    public required string            Label    { get; init; }
    public string?                    Shortcut { get; init; }
    public FileSystemVolume?          Volume   { get; init; }
    public SftpConnectionInfo?        SftpConnection { get; init; }
    public FtpConnectionInfo?         FtpConnection { get; init; }
    public VolumeSelectionAction      Action   { get; init; }
}
