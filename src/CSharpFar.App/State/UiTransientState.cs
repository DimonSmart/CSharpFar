using CSharpFar.Console.Models;
namespace CSharpFar.App.State;

internal sealed class UiTransientState
{
    public ConsoleViewport? LastRenderViewport { get; set; }

    // The committed hidden frame belongs to an older viewport after scrollback moves.
    // It remains visual history only; pointer and completion interaction must not use it.
    public bool HiddenUiDetachedByScroll { get; set; }
}
