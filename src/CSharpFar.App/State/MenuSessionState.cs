using CSharpFar.Core.Menu;

namespace CSharpFar.App.State;

internal sealed class MenuSessionState
{
    public required MenuState State { get; init; }
}
