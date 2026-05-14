using System.Globalization;
using System.Text;

namespace CSharpFar.Core.Text;

public static class TextEncodingDetector
{
    private const int BinarySampleSize = 8192;

    static TextEncodingDetector()
    {
        EnsureCodePagesProviderRegistered();
    }

    public static void EnsureCodePagesProviderRegistered() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static EncodingDetectionResult Detect(
        ReadOnlySpan<byte> bytes,
        TextEncodingSelection? selection = null)
    {
        selection ??= TextEncodingSelection.Automatic;
        if (selection.Kind == TextEncodingSelectionKind.Explicit)
            return DetectExplicit(bytes, selection);

        if (StartsWith(bytes, [0xEF, 0xBB, 0xBF]))
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: false);
            return new EncodingDetectionResult(
                encoding,
                ContentStartLength: 3,
                IsBinary: false,
                IsUtf16: false,
                IsUtf16BigEndian: false,
                HasByteOrderMark: true,
                DisplayName: "UTF-8 BOM",
                Selection: TextEncodingSelection.Automatic);
        }

        if (StartsWith(bytes, [0xFF, 0xFE]))
        {
            var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: false);
            return new EncodingDetectionResult(
                encoding,
                ContentStartLength: 2,
                IsBinary: false,
                IsUtf16: true,
                IsUtf16BigEndian: false,
                HasByteOrderMark: true,
                DisplayName: "UTF-16 LE BOM",
                Selection: TextEncodingSelection.Automatic);
        }

        if (StartsWith(bytes, [0xFE, 0xFF]))
        {
            var encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: false);
            return new EncodingDetectionResult(
                encoding,
                ContentStartLength: 2,
                IsBinary: false,
                IsUtf16: true,
                IsUtf16BigEndian: true,
                HasByteOrderMark: true,
                DisplayName: "UTF-16 BE BOM",
                Selection: TextEncodingSelection.Automatic);
        }

        if (LooksLikeUtf16(bytes, bigEndian: false))
        {
            var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: false);
            return new EncodingDetectionResult(
                encoding,
                ContentStartLength: 0,
                IsBinary: false,
                IsUtf16: true,
                IsUtf16BigEndian: false,
                HasByteOrderMark: false,
                DisplayName: "UTF-16 LE",
                Selection: TextEncodingSelection.Automatic);
        }

        if (LooksLikeUtf16(bytes, bigEndian: true))
        {
            var encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: false);
            return new EncodingDetectionResult(
                encoding,
                ContentStartLength: 0,
                IsBinary: false,
                IsUtf16: true,
                IsUtf16BigEndian: true,
                HasByteOrderMark: false,
                DisplayName: "UTF-16 BE",
                Selection: TextEncodingSelection.Automatic);
        }

        bool isBinary = LooksBinary(bytes);
        if (!isBinary && IsValidUtf8(bytes))
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            return new EncodingDetectionResult(
                encoding,
                ContentStartLength: 0,
                IsBinary: false,
                IsUtf16: false,
                IsUtf16BigEndian: false,
                HasByteOrderMark: false,
                DisplayName: "UTF-8",
                Selection: TextEncodingSelection.Automatic);
        }

        int ansiCodePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        Encoding fallback = Encoding.GetEncoding(ansiCodePage);
        return new EncodingDetectionResult(
            fallback,
            ContentStartLength: 0,
            IsBinary: isBinary,
            IsUtf16: false,
            IsUtf16BigEndian: false,
            HasByteOrderMark: false,
            DisplayName: $"Windows ANSI ({ansiCodePage})",
            Selection: TextEncodingSelection.Automatic);
    }

    private static EncodingDetectionResult DetectExplicit(
        ReadOnlySpan<byte> bytes,
        TextEncodingSelection selection)
    {
        Encoding encoding = ResolveExplicitEncoding(selection);
        int contentStartLength = MatchingPreambleLength(bytes, encoding);
        bool isUtf16 = IsUtf16(encoding);
        bool isUtf16BigEndian = IsUtf16BigEndian(encoding);

        return new EncodingDetectionResult(
            encoding,
            contentStartLength,
            IsBinary: false,
            isUtf16,
            isUtf16BigEndian,
            HasByteOrderMark: contentStartLength > 0,
            DisplayName: DisplayName(encoding),
            Selection: selection);
    }

    private static Encoding ResolveExplicitEncoding(TextEncodingSelection selection)
    {
        EnsureCodePagesProviderRegistered();
        return selection.CodePage.HasValue
            ? Encoding.GetEncoding(selection.CodePage.Value)
            : Encoding.GetEncoding(selection.EncodingName!);
    }

    private static int MatchingPreambleLength(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        int knownPreambleLength = encoding.CodePage switch
        {
            65001 when StartsWith(bytes, [0xEF, 0xBB, 0xBF]) => 3,
            1200 when StartsWith(bytes, [0xFF, 0xFE]) => 2,
            1201 when StartsWith(bytes, [0xFE, 0xFF]) => 2,
            _ => 0,
        };
        if (knownPreambleLength > 0)
            return knownPreambleLength;

        byte[] preamble = encoding.GetPreamble();
        if (preamble.Length == 0)
            return 0;

        return StartsWith(bytes, preamble) ? preamble.Length : 0;
    }

    private static string DisplayName(Encoding encoding) =>
        encoding.CodePage switch
        {
            65001 => "UTF-8",
            1200 => "UTF-16 LE",
            1201 => "UTF-16 BE",
            1251 => "Windows-1251",
            1252 => "Windows-1252",
            866 => "CP866",
            _ => encoding.EncodingName,
        };

    private static bool IsUtf16(Encoding encoding) =>
        encoding.CodePage is 1200 or 1201;

    private static bool IsUtf16BigEndian(Encoding encoding) =>
        encoding.CodePage == 1201;

    private static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> prefix)
    {
        if (bytes.Length < prefix.Length)
            return false;

        return bytes[..prefix.Length].SequenceEqual(prefix);
    }

    private static bool LooksLikeUtf16(ReadOnlySpan<byte> bytes, bool bigEndian)
    {
        int pairs = Math.Min(bytes.Length / 2, 4096);
        if (pairs < 8)
            return false;

        int zeroCount = 0;
        int printableCount = 0;
        for (int pair = 0; pair < pairs; pair++)
        {
            byte first = bytes[pair * 2];
            byte second = bytes[pair * 2 + 1];
            byte high = bigEndian ? first : second;
            byte low = bigEndian ? second : first;
            if (high == 0)
                zeroCount++;
            if (low is >= 0x09 and <= 0x7E)
                printableCount++;
        }

        return zeroCount > pairs * 3 / 5 && printableCount > pairs / 2;
    }

    private static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return false;

        int controlCount = 0;
        int checkedBytes = Math.Min(bytes.Length, BinarySampleSize);
        for (int i = 0; i < checkedBytes; i++)
        {
            byte value = bytes[i];
            if (value == 0)
                return true;
            if (value < 0x20 && value is not 0x09 and not 0x0A and not 0x0D and not 0x1B)
                controlCount++;
        }

        return controlCount > checkedBytes / 20;
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
