using System.Globalization;
using System.Text.RegularExpressions;

namespace CSharpFar.App.Viewer;

internal static class ViewerSearchEngine
{
    private const int TextSearchMaxBytesPerLine = 4 * 1024 * 1024;
    private const int HexSearchChunkBytes = 64 * 1024;

    public static ViewerSearchMatch? Find(
        IFileByteReader reader,
        LargeFileViewerState state,
        ViewerSearchRequest request,
        bool searchBackward)
    {
        if (string.IsNullOrEmpty(request.Pattern))
            return null;

        return request.SearchHex
            ? FindHex(reader, state, request, searchBackward)
            : FindText(reader, state, request, searchBackward);
    }

    public static bool TryParseHexPattern(string pattern, out byte[] bytes, out string? error)
    {
        string compact = pattern
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (compact.Length == 0)
        {
            bytes = [];
            error = "Hex sequence is required.";
            return false;
        }

        if (compact.Length % 2 != 0)
        {
            bytes = [];
            error = "Hex sequence must contain an even number of digits.";
            return false;
        }

        bytes = new byte[compact.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            string token = compact.Substring(i * 2, 2);
            if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                error = "Hex sequence can contain only 0-9 and A-F digits.";
                bytes = [];
                return false;
            }
        }

        error = null;
        return true;
    }

    private static ViewerSearchMatch? FindText(
        IFileByteReader reader,
        LargeFileViewerState state,
        ViewerSearchRequest request,
        bool searchBackward)
    {
        return searchBackward
            ? FindTextBackward(reader, state, request)
            : FindTextForward(reader, state, request);
    }

    private static ViewerSearchMatch? FindTextForward(
        IFileByteReader reader,
        LargeFileViewerState state,
        ViewerSearchRequest request)
    {
        long startOffset = state.SearchMatch is { IsHex: false } match
            ? match.LineStartOffset
            : state.TopByteOffset;
        int firstLineStartIndex = state.SearchMatch is { IsHex: false } previous
            ? previous.CharacterIndex + Math.Max(1, previous.CharacterLength)
            : 0;

        startOffset = state.LineScanner
            .FindLineStartAtOrBeforeAsync(startOffset)
            .GetAwaiter()
            .GetResult();

        long offset = startOffset;
        while (offset < reader.Length)
        {
            var scanned = state.LineScanner
                .ReadLinesAsync(offset, 1, TextSearchMaxBytesPerLine)
                .GetAwaiter()
                .GetResult();
            if (scanned.Lines.Count == 0)
                return null;

            var line = scanned.Lines[0];
            int lineStartIndex = line.StartOffset == startOffset ? firstLineStartIndex : 0;
            var found = FindInLine(line.Text, request, lineStartIndex, int.MaxValue, searchBackward: false);
            if (found is not null)
                return CreateTextMatch(line, found.Value);

            if (line.NextOffset <= offset)
                return null;

            offset = line.NextOffset;
        }

        return null;
    }

    private static ViewerSearchMatch? FindTextBackward(
        IFileByteReader reader,
        LargeFileViewerState state,
        ViewerSearchRequest request)
    {
        long targetLineStart = state.SearchMatch is { IsHex: false } match
            ? match.LineStartOffset
            : state.LineScanner
                .FindLineStartAtOrBeforeAsync(state.TopByteOffset)
                .GetAwaiter()
                .GetResult();
        int targetCharacterLimit = state.SearchMatch is { IsHex: false } previous
            ? previous.CharacterIndex
            : int.MaxValue;

        long offset = state.LineScanner.ContentStartOffset;
        ViewerSearchMatch? last = null;
        while (offset < reader.Length && offset <= targetLineStart)
        {
            var scanned = state.LineScanner
                .ReadLinesAsync(offset, 1, TextSearchMaxBytesPerLine)
                .GetAwaiter()
                .GetResult();
            if (scanned.Lines.Count == 0)
                break;

            var line = scanned.Lines[0];
            int characterLimit = line.StartOffset == targetLineStart
                ? targetCharacterLimit
                : int.MaxValue;
            var found = FindInLine(line.Text, request, 0, characterLimit, searchBackward: true);
            if (found is not null)
                last = CreateTextMatch(line, found.Value);

            if (line.NextOffset <= offset)
                break;

            offset = line.NextOffset;
        }

        return last;
    }

    private static (int Index, int Length)? FindInLine(
        string line,
        ViewerSearchRequest request,
        int startIndex,
        int characterLimit,
        bool searchBackward)
    {
        int safeStart = Math.Clamp(startIndex, 0, line.Length);
        int safeLimit = Math.Clamp(characterLimit, 0, line.Length);
        if (safeStart > safeLimit)
            return null;

        if (request.UseRegex)
            return FindRegexInLine(line, request, safeStart, safeLimit, searchBackward);

        return FindLiteralInLine(line, request, safeStart, safeLimit, searchBackward);
    }

    private static (int Index, int Length)? FindLiteralInLine(
        string line,
        ViewerSearchRequest request,
        int startIndex,
        int characterLimit,
        bool searchBackward)
    {
        var comparison = request.CaseSensitive
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;

        if (searchBackward)
        {
            int searchFrom = Math.Min(characterLimit - 1, line.Length - 1);
            if (searchFrom < 0)
                return null;

            while (searchFrom >= 0)
            {
                int found = line.LastIndexOf(
                    request.Pattern,
                    searchFrom,
                    comparison);
                if (found < 0)
                    return null;

                int end = found + request.Pattern.Length;
                if (end <= characterLimit &&
                    (!request.WholeWords || IsWholeWord(line, found, end)))
                {
                    return (found, request.Pattern.Length);
                }

                searchFrom = found - 1;
            }

            return null;
        }

        int index = startIndex;
        while (index <= characterLimit - request.Pattern.Length)
        {
            int found = line.IndexOf(request.Pattern, index, comparison);
            if (found < 0)
                return null;

            int end = found + request.Pattern.Length;
            if (end <= characterLimit &&
                (!request.WholeWords || IsWholeWord(line, found, end)))
            {
                return (found, request.Pattern.Length);
            }

            index = Math.Max(end, found + 1);
        }

        return null;
    }

    private static (int Index, int Length)? FindRegexInLine(
        string line,
        ViewerSearchRequest request,
        int startIndex,
        int characterLimit,
        bool searchBackward)
    {
        var options = RegexOptions.CultureInvariant;
        if (!request.CaseSensitive)
            options |= RegexOptions.IgnoreCase;

        Match? selected = null;
        foreach (Match match in Regex.Matches(line, request.Pattern, options))
        {
            if (!match.Success || match.Length == 0)
                continue;
            if (match.Index < startIndex || match.Index + match.Length > characterLimit)
                continue;
            if (request.WholeWords && !IsWholeWord(line, match.Index, match.Index + match.Length))
                continue;

            selected = match;
            if (!searchBackward)
                break;
        }

        return selected is null ? null : (selected.Index, selected.Length);
    }

    private static ViewerSearchMatch CreateTextMatch(
        ScannedLine line,
        (int Index, int Length) found) =>
        new(
            line.StartOffset,
            line.StartOffset,
            found.Index,
            found.Length,
            line.StartOffset,
            0,
            line.Text.Substring(found.Index, found.Length),
            IsHex: false);

    private static bool IsWholeWord(string text, int start, int end)
    {
        bool before = start == 0 || !IsWordCharacter(text[start - 1]);
        bool after = end >= text.Length || !IsWordCharacter(text[end]);
        return before && after;
    }

    private static bool IsWordCharacter(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    private static ViewerSearchMatch? FindHex(
        IFileByteReader reader,
        LargeFileViewerState state,
        ViewerSearchRequest request,
        bool searchBackward)
    {
        if (!TryParseHexPattern(request.Pattern, out var needle, out _))
            return null;

        return searchBackward
            ? FindHexBackward(reader, state, needle)
            : FindHexForward(reader, state, needle);
    }

    private static ViewerSearchMatch? FindHexForward(
        IFileByteReader reader,
        LargeFileViewerState state,
        byte[] needle)
    {
        long startOffset = state.SearchMatch is { IsHex: true } match
            ? match.ByteOffset + 1
            : state.TopByteOffset;

        startOffset = Math.Clamp(startOffset, 0, reader.Length);
        int overlapLength = Math.Max(0, needle.Length - 1);
        byte[] overlap = [];
        long offset = startOffset;

        while (offset < reader.Length)
        {
            int requested = (int)Math.Min(HexSearchChunkBytes, reader.Length - offset);
            var chunk = new byte[requested];
            int read = state.BlockCache.ReadAsync(offset, chunk).GetAwaiter().GetResult();
            if (read == 0)
                break;

            var combined = new byte[overlap.Length + read];
            overlap.CopyTo(combined, 0);
            Array.Copy(chunk, 0, combined, overlap.Length, read);

            int found = IndexOf(combined, needle);
            if (found >= 0)
            {
                long absolute = offset - overlap.Length + found;
                if (absolute >= startOffset)
                    return CreateHexMatch(absolute, needle);
            }

            if (overlapLength > 0)
            {
                int nextOverlap = Math.Min(overlapLength, combined.Length);
                overlap = new byte[nextOverlap];
                Array.Copy(combined, combined.Length - nextOverlap, overlap, 0, nextOverlap);
            }

            offset += read;
        }

        return null;
    }

    private static ViewerSearchMatch? FindHexBackward(
        IFileByteReader reader,
        LargeFileViewerState state,
        byte[] needle)
    {
        long limit = state.SearchMatch is { IsHex: true } match
            ? match.ByteOffset
            : state.TopByteOffset;
        limit = Math.Clamp(limit, 0, reader.Length);

        ViewerSearchMatch? last = null;
        long offset = 0;
        int overlapLength = Math.Max(0, needle.Length - 1);
        byte[] overlap = [];

        while (offset < limit)
        {
            int requested = (int)Math.Min(HexSearchChunkBytes, limit - offset);
            var chunk = new byte[requested];
            int read = state.BlockCache.ReadAsync(offset, chunk).GetAwaiter().GetResult();
            if (read == 0)
                break;

            var combined = new byte[overlap.Length + read];
            overlap.CopyTo(combined, 0);
            Array.Copy(chunk, 0, combined, overlap.Length, read);

            int searchStart = 0;
            while (true)
            {
                int found = IndexOf(combined, needle, searchStart);
                if (found < 0)
                    break;

                long absolute = offset - overlap.Length + found;
                if (absolute < limit)
                    last = CreateHexMatch(absolute, needle);

                searchStart = found + 1;
            }

            if (overlapLength > 0)
            {
                int nextOverlap = Math.Min(overlapLength, combined.Length);
                overlap = new byte[nextOverlap];
                Array.Copy(combined, combined.Length - nextOverlap, overlap, 0, nextOverlap);
            }

            offset += read;
        }

        return last;
    }

    private static ViewerSearchMatch CreateHexMatch(long byteOffset, byte[] needle) =>
        new(
            byteOffset / 16 * 16,
            byteOffset / 16 * 16,
            0,
            0,
            byteOffset,
            needle.Length,
            string.Join(' ', needle.Select(value => value.ToString("X2", CultureInfo.InvariantCulture))),
            IsHex: true);

    private static int IndexOf(byte[] haystack, byte[] needle, int startIndex = 0)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
            return -1;

        for (int i = Math.Max(0, startIndex); i <= haystack.Length - needle.Length; i++)
        {
            bool equal = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j])
                    continue;

                equal = false;
                break;
            }

            if (equal)
                return i;
        }

        return -1;
    }
}
