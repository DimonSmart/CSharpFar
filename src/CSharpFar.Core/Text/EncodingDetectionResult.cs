using System.Text;

namespace CSharpFar.Core.Text;

public sealed record EncodingDetectionResult(
    Encoding Encoding,
    int ContentStartLength,
    bool IsBinary,
    bool IsUtf16,
    bool IsUtf16BigEndian,
    bool HasByteOrderMark,
    string DisplayName,
    TextEncodingSelection Selection);
