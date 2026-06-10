using CSharpFar.App.CommandLine;
using CSharpFar.Core.Models;

namespace CSharpFar.App.State;

internal sealed class CommandLineSessionState
{
    public required CommandLineState State { get; init; }

    public required CommandCompletionState Completion { get; init; }
}
