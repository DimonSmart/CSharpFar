namespace CSharpFar.Core.Abstractions;

public interface IShellService
{
    Task ExecuteAsync(string command, string workingDirectory, CancellationToken cancellationToken = default);
}
