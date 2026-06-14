namespace CSharpFar.Core.Abstractions;

public interface IFileOperationErrorSink
{
    void AddError(string path, string message);
}
