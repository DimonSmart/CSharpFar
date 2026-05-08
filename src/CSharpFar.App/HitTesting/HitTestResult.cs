using CSharpFar.Core.Models;

namespace CSharpFar.App.HitTesting;

public sealed record HitTestResult(
    UiElementKind ElementKind,
    object?       Target,
    int           LocalX,
    int           LocalY);

public enum UiElementKind
{
    None,
    PanelBorder,
    PanelTitle,
    PanelItem,
    PanelStatusLine,
    PanelScrollbar,
    CommandLine,
    KeyBar,
    Dialog,
}

public sealed record PanelItemHitTarget(
    PanelSide      PanelSide,
    int            VisibleRowIndex,
    int            ItemIndex,
    FilePanelItem  Item);
