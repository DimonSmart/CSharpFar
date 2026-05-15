using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed record PopupRenderOptions
{
    public bool DrawBorder { get; init; } = true;
    public bool DrawShadow { get; init; } = true;
    public bool DrawDoubleBorder { get; init; }
    public required CellStyle BorderStyle { get; init; }
    public required CellStyle BackgroundStyle { get; init; }
    public required CellStyle ShadowStyle { get; init; }
    public CellStyle? TitleStyle { get; init; }
    public ScrollState? VerticalScrollState { get; init; }
    public int ShadowOffsetX { get; init; } = 1;
    public int ShadowOffsetY { get; init; } = 1;
}
