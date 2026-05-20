using CSharpFar.Core.Abstractions;

namespace CSharpFar.Ui;

public sealed class TextCopyTextClipboard : ITextClipboard
{
    public static TextCopyTextClipboard Instance { get; } = new();

    private TextCopyTextClipboard()
    {
    }

    public bool TrySetText(string text)
    {
        try
        {
            TextCopy.ClipboardService.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryGetText(out string text)
    {
        try
        {
            text = TextCopy.ClipboardService.GetText() ?? string.Empty;
            return true;
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }
}
