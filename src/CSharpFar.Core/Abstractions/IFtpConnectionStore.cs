using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFtpConnectionStore
{
    IReadOnlyList<FtpConnectionInfo> Load();
    void Save(IReadOnlyList<FtpConnectionInfo> connections);
}
