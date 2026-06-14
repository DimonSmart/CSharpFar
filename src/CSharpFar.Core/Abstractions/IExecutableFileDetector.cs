namespace CSharpFar.Core.Abstractions;

public interface IExecutableFileDetector
{
    bool IsExecutableFile(string path);
}
