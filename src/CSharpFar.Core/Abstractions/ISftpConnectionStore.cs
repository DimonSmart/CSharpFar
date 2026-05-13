using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface ISftpConnectionStore
{
    IReadOnlyList<SftpConnectionInfo> Load();
    void Save(IReadOnlyList<SftpConnectionInfo> connections);
}
