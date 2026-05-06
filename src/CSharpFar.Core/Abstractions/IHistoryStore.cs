using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IHistoryStore
{
    IReadOnlyList<CommandHistoryItem> GetCommandHistory();
    void AddCommand(CommandHistoryItem item);

    IReadOnlyList<DirectoryHistoryItem> GetDirectoryHistory();
    void AddDirectory(DirectoryHistoryItem item);

    IReadOnlyList<FileHistoryItem> GetFileHistory();
    void AddFile(FileHistoryItem item);
}
