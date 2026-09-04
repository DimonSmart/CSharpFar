namespace CSharpFar.Ui;

public interface ITextClipboard
{
    bool TrySetText(string text);
    bool TryGetText(out string text);
}
