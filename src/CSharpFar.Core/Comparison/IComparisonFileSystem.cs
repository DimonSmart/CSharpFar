namespace CSharpFar.Core.Comparison;

public interface IComparisonFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IEnumerable<string> EnumerateFileSystemEntries(string path);
    FileAttributes GetAttributes(string path);
    long GetFileSize(string path);
    DateTime GetLastWriteTimeUtc(string path);
    Stream OpenRead(string path);
}

public class LocalComparisonFileSystem : IComparisonFileSystem
{
    public virtual bool FileExists(string path) => File.Exists(path);
    public virtual bool DirectoryExists(string path) => Directory.Exists(path);
    public virtual IEnumerable<string> EnumerateFileSystemEntries(string path) => Directory.EnumerateFileSystemEntries(path);
    public virtual FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public virtual long GetFileSize(string path) => new FileInfo(path).Length;
    public virtual DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
    public virtual Stream OpenRead(string path) => File.OpenRead(path);
}
