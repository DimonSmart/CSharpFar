using CSharpFar.Core.Abstractions;

namespace CSharpFar.Tests.Fakes;

public sealed class NoOpShellService : IShellService
{
    public void Execute(string command, string workingDirectory)
    {
    }
}
