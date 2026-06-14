using System.Diagnostics;

namespace CSharpFar.Core.Abstractions;

public interface IShellCommandLineBuilder
{
    ProcessStartInfo CreateStartInfo(string command, string workingDirectory);
}
