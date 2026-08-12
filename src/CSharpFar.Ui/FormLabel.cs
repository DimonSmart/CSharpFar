namespace CSharpFar.Ui;

internal readonly record struct FormLabel(string Text, char? Mnemonic);

internal static class FormLabelParser
{
    public static FormLabel Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int index = text.IndexOf('&');
        return index >= 0 && index + 1 < text.Length
            ? new FormLabel(text.Remove(index, 1), char.ToUpperInvariant(text[index + 1]))
            : new FormLabel(text, null);
    }
}
