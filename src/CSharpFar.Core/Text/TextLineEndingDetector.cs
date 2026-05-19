namespace CSharpFar.Core.Text;

public static class TextLineEndingDetector
{
    public static TextLineEndingKind Detect(ReadOnlySpan<char> text)
    {
        bool hasCrLf = false;
        bool hasLf = false;
        bool hasCr = false;

        for (int index = 0; index < text.Length; index++)
        {
            char ch = text[index];
            if (ch == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    hasCrLf = true;
                    index++;
                }
                else
                {
                    hasCr = true;
                }
            }
            else if (ch == '\n')
            {
                hasLf = true;
            }
        }

        int kindCount = (hasCrLf ? 1 : 0) + (hasLf ? 1 : 0) + (hasCr ? 1 : 0);
        if (kindCount == 0)
            return TextLineEndingKind.None;
        if (kindCount > 1)
            return TextLineEndingKind.Mixed;
        if (hasCrLf)
            return TextLineEndingKind.CrLf;
        return hasLf ? TextLineEndingKind.Lf : TextLineEndingKind.Cr;
    }

    public static TextLineEndingKind DetectBytes(ReadOnlySpan<byte> bytes)
    {
        bool hasCrLf = false;
        bool hasLf = false;
        bool hasCr = false;

        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            if (value == 0x0D)
            {
                if (index + 1 < bytes.Length && bytes[index + 1] == 0x0A)
                {
                    hasCrLf = true;
                    index++;
                }
                else
                {
                    hasCr = true;
                }
            }
            else if (value == 0x0A)
            {
                hasLf = true;
            }
        }

        int kindCount = (hasCrLf ? 1 : 0) + (hasLf ? 1 : 0) + (hasCr ? 1 : 0);
        if (kindCount == 0)
            return TextLineEndingKind.None;
        if (kindCount > 1)
            return TextLineEndingKind.Mixed;
        if (hasCrLf)
            return TextLineEndingKind.CrLf;
        return hasLf ? TextLineEndingKind.Lf : TextLineEndingKind.Cr;
    }

    public static string DisplayName(TextLineEndingKind kind) =>
        kind switch
        {
            TextLineEndingKind.CrLf => "Windows (CRLF)",
            TextLineEndingKind.Lf => "Unix (LF)",
            TextLineEndingKind.Cr => "Classic Mac (CR)",
            TextLineEndingKind.Mixed => "Mixed",
            _ => string.Empty,
        };
}
