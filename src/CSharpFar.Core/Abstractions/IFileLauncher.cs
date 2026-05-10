using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileLauncher
{
    /// <summary>Returns how the file should be launched.</summary>
    FileLaunchMode GetLaunchMode(string fullPath);

    /// <summary>Opens or runs the file from the given working directory.</summary>
    void OpenFile(string fullPath, string workingDirectory);
}
