namespace CSharpFar.Core.Abstractions;

public interface IFileLauncher
{
    /// <summary>Opens the file through the operating system default action.</summary>
    void OpenFile(string fullPath);
}
