using CSharpFar.Ui;

namespace CSharpFar.Module.Sftp;

internal static class SftpTextHistoryIds
{
    public static readonly TextHistoryId ConnectionName = new("SftpConnectionDialog.ConnectionName");
    public static readonly TextHistoryId Host = new("SftpConnectionDialog.Host");
    public static readonly TextHistoryId Port = new("SftpConnectionDialog.Port");
    public static readonly TextHistoryId UserName = new("SftpConnectionDialog.UserName");
    public static readonly TextHistoryId RemoteRoot = new("SftpConnectionDialog.RemoteRoot");
}
