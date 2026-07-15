using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.State;

internal sealed class UiTransientState
{
    public ConsoleViewport? LastRenderViewport { get; set; }

    public PanelScrollbarDrag? PanelScrollbarDrag { get; set; }
}

internal readonly record struct PanelScrollbarDrag(PanelSide Side, ScrollBarDragState DragState);
