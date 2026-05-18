namespace CSharpFar.Module.Ftp;

public interface IFtpConnectionStore
{
    IReadOnlyList<FtpConnectionInfo> Load();
    void Save(IReadOnlyList<FtpConnectionInfo> connections);
}
