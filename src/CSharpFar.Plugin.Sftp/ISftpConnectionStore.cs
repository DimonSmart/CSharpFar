namespace CSharpFar.Plugin.Sftp;

public interface ISftpConnectionStore
{
    IReadOnlyList<SftpConnectionInfo> Load();
    void Save(IReadOnlyList<SftpConnectionInfo> connections);
}
