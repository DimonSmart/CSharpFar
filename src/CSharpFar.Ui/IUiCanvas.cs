using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public interface IUiCanvas
{
    void Write(int x, int y, string text, CellStyle style);
    void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style);
    void WriteForced(int x, int y, string text, CellStyle style);
    void WriteForced(int x, int y, ReadOnlySpan<char> text, CellStyle style);
    void WriteChar(int x, int y, char ch, CellStyle style);
    void FillRegion(Rect region, CellStyle style);
    void DrawBox(Rect rect, CellStyle style);
    void DrawDoubleBox(Rect rect, CellStyle style);
    ConsoleSize Size { get; }
}

public static class UiCanvas
{
    public static IUiCanvas From(ScreenRenderer renderer) => new ScreenRendererCanvas(renderer);
}

internal sealed class ScreenRendererCanvas(ScreenRenderer renderer) : IUiCanvas
{
    public void Write(int x, int y, string text, CellStyle style) => renderer.Write(x, y, text, style);
    public void Write(int x, int y, ReadOnlySpan<char> text, CellStyle style) => renderer.Write(x, y, text, style);
    public void WriteForced(int x, int y, string text, CellStyle style) => renderer.WriteForced(x, y, text, style);
    public void WriteForced(int x, int y, ReadOnlySpan<char> text, CellStyle style) => renderer.WriteForced(x, y, text, style);
    public void WriteChar(int x, int y, char ch, CellStyle style) => renderer.WriteChar(x, y, ch, style);
    public void FillRegion(Rect region, CellStyle style) => renderer.FillRegion(region, style);
    public void DrawBox(Rect rect, CellStyle style) => renderer.DrawBox(rect, style);
    public void DrawDoubleBox(Rect rect, CellStyle style) => renderer.DrawDoubleBox(rect, style);
    public ConsoleSize Size => renderer.FrameViewport.Size;
}
