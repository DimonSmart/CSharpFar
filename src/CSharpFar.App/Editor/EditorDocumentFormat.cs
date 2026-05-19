using System.Text;

namespace CSharpFar.App.Editor;

public sealed class EditorDocumentFormat
{
    public EditorDocumentFormat(
        Encoding encoding,
        bool emitByteOrderMark,
        EditorLineEnding lineEnding,
        string encodingDisplayName)
    {
        Encoding = encoding;
        EmitByteOrderMark = emitByteOrderMark;
        LineEnding = lineEnding;
        EncodingDisplayName = encodingDisplayName;
    }

    public Encoding Encoding { get; }
    public bool EmitByteOrderMark { get; }
    public EditorLineEnding LineEnding { get; }
    public string EncodingDisplayName { get; }

    public EditorDocumentFormat WithEncoding(Encoding encoding, bool emitByteOrderMark, string displayName) =>
        new(encoding, emitByteOrderMark, LineEnding, displayName);

    public EditorDocumentFormat WithLineEnding(EditorLineEnding lineEnding) =>
        new(Encoding, EmitByteOrderMark, lineEnding, EncodingDisplayName);

    public string LineEndingDisplayName =>
        LineEnding switch
        {
            EditorLineEnding.CrLf => "Windows (CRLF)",
            EditorLineEnding.Lf => "Unix (LF)",
            EditorLineEnding.Cr => "Classic Mac (CR)",
            EditorLineEnding.Mixed => "Mixed",
            _ => LineEnding.ToString(),
        };

    public string BomDisplayName => EmitByteOrderMark ? "BOM" : "No BOM";

    internal Encoding CreateSaveEncoding() =>
        Encoding.CodePage switch
        {
            65001 => new UTF8Encoding(EmitByteOrderMark, throwOnInvalidBytes: false),
            1200 => new UnicodeEncoding(bigEndian: false, EmitByteOrderMark, throwOnInvalidBytes: false),
            1201 => new UnicodeEncoding(bigEndian: true, EmitByteOrderMark, throwOnInvalidBytes: false),
            _ => Encoding.GetEncoding(Encoding.CodePage),
        };

    internal static string Separator(EditorLineEnding lineEnding) =>
        lineEnding switch
        {
            EditorLineEnding.CrLf => "\r\n",
            EditorLineEnding.Lf => "\n",
            EditorLineEnding.Cr => "\r",
            _ => "\n",
        };
}
