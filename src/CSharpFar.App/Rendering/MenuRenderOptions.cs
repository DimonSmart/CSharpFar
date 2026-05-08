using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

public sealed record MenuRenderOptions
{
    public required CellStyle NormalStyle { get; init; }
    public required CellStyle ActiveStyle { get; init; }
    public required CellStyle DisabledStyle { get; init; }
    public required CellStyle BorderStyle { get; init; }
    public required CellStyle ShadowStyle { get; init; }
}
