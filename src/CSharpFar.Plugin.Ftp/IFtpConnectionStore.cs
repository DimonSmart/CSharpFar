namespace CSharpFar.Plugin.Ftp;

public interface IFtpConnectionStore
{
    IReadOnlyList<FtpConnectionInfo> Load();
    void Save(IReadOnlyList<FtpConnectionInfo> connections);
}
