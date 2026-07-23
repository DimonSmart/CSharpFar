using CSharpFar.Console.Models;
namespace CSharpFar.App.State;

internal sealed class UiTransientState
{
    public ConsoleViewport? LastRenderViewport { get; set; }
}
