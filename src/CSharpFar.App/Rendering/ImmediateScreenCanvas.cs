using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ImmediateScreenCanvas(ScreenRenderer screen) : IUiCanvas
{
    public void Write(int x, int y, string text, CellStyle style) => screen.Write(x, y, text, style);
    public void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style) => screen.Write(x, y, text, style);
    public void WriteForced(int x, int y, string text, CellStyle style) => screen.WriteForced(x, y, text, style);
    public void WriteForced(int x, int y, ReadOnlySpan<char> text, CellStyle style) => screen.WriteForced(x, y, text, style);
    public void WriteChar(int x, int y, char ch, CellStyle style) => screen.WriteChar(x, y, ch, style);
    public void FillRegion(Rect region, CellStyle style) => screen.FillRegion(region, style);
    public void DrawBox(Rect rect, CellStyle style) => screen.DrawBox(rect, style);
    public void DrawDoubleBox(Rect rect, CellStyle style) => screen.DrawDoubleBox(rect, style);
    public ConsoleSize Size => screen.GetSize();
}
