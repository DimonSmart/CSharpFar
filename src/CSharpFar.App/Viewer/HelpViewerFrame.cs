using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed record HelpViewerFrame(
    ConsoleViewport Viewport,
    Rect HeaderBounds,
    ScrollableViewportFrameState VerticalViewport,
    IReadOnlyList<FunctionKeyBarActionHit<HelpAction>> FooterActionHits,
    Rect FunctionKeyBarBounds,
    int ScrollLeft,
    int MaxScrollTop,
    int MaxScrollLeft,
    int VisibleRows)
{
    public Rect ContentBounds => VerticalViewport.ContentBounds;

    public int ScrollTop => VerticalViewport.FirstVisibleIndex;

    public Rect? ScrollBarBounds => VerticalViewport.ScrollbarBounds;

    public ScrollState? VerticalScrollState => ScrollBarBounds is null
        ? null
        : new ScrollState
        {
            TotalItems = VerticalViewport.TotalItems,
            ViewportItems = VerticalViewport.ViewportItems,
            FirstVisibleIndex = VerticalViewport.FirstVisibleIndex,
        };
}
