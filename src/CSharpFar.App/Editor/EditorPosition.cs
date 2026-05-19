namespace CSharpFar.App.Editor;

public readonly record struct EditorPosition(int Line, int Column)
{
    public static EditorPosition Start { get; } = new(0, 0);
}
