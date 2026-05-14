namespace CSharpFar.Core.Text;

public sealed record TextEncodingSelection
{
    private TextEncodingSelection(
        TextEncodingSelectionKind kind,
        int? codePage,
        string? encodingName)
    {
        Kind = kind;
        CodePage = codePage;
        EncodingName = encodingName;
    }

    public TextEncodingSelectionKind Kind { get; }
    public int? CodePage { get; }
    public string? EncodingName { get; }

    public static TextEncodingSelection Automatic { get; } =
        new(TextEncodingSelectionKind.Automatic, null, null);

    public static TextEncodingSelection Explicit(int codePage)
    {
        if (codePage <= 0)
            throw new ArgumentOutOfRangeException(nameof(codePage));

        return new TextEncodingSelection(TextEncodingSelectionKind.Explicit, codePage, null);
    }

    public static TextEncodingSelection Explicit(string encodingName)
    {
        if (string.IsNullOrWhiteSpace(encodingName))
            throw new ArgumentException("Encoding name must not be empty.", nameof(encodingName));

        return new TextEncodingSelection(TextEncodingSelectionKind.Explicit, null, encodingName.Trim());
    }
}
