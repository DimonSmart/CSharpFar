using CSharpFar.Ui;

namespace CSharpFar.Module.Ftp;

internal static class FtpTextHistoryIds
{
    public static readonly TextHistoryId ConnectionName = new("FtpConnectionDialog.ConnectionName");
    public static readonly TextHistoryId Host = new("FtpConnectionDialog.Host");
    public static readonly TextHistoryId Port = new("FtpConnectionDialog.Port");
    public static readonly TextHistoryId UserName = new("FtpConnectionDialog.UserName");
    public static readonly TextHistoryId RemoteRoot = new("FtpConnectionDialog.RemoteRoot");
    public static readonly TextHistoryId ActivePorts = new("FtpConnectionDialog.ActivePorts");
}
