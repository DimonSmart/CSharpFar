using System.Text;

namespace CSharpFar.App.Viewer;

internal sealed record MarkdownInlineTransform(
    string Text,
    IReadOnlyList<PresentedSourceSpan> SourceSpans,
    IReadOnlyList<PresentedStyleSpan> StyleSpans,
    bool Changed);

internal static class MarkdownInlinePresentation
{
    public static MarkdownInlineTransform Transform(string source, int sourceOffset = 0)
    {
        if (string.IsNullOrEmpty(source))
            return Raw(source, sourceOffset);

        if (source.Length > MarkdownViewerPresentationProvider.MaxPresentedLineChars)
            return Raw(source, sourceOffset);

        IReadOnlyDictionary<int, int> codeClosings = BuildCodeClosings(source);
        var text = new StringBuilder(source.Length);
        var sourceSpans = new List<PresentedSourceSpan>();
        var styleSpans = new List<PresentedStyleSpan>();
        int rawStart = 0;
        int index = 0;
        bool changed = false;

        while (index < source.Length)
        {
            if (!TryReadConstruct(source, index, codeClosings, out var construct))
            {
                index++;
                continue;
            }

            AppendSourceRange(source, sourceOffset, rawStart, index - rawStart, text, sourceSpans);

            int presentedStart = text.Length;
            AppendSourceRange(
                source,
                sourceOffset,
                construct.ContentStart,
                construct.ContentLength,
                text,
                sourceSpans);
            styleSpans.Add(new PresentedStyleSpan(presentedStart, construct.ContentLength, construct.Style));

            changed = true;
            index = construct.EndExclusive;
            rawStart = index;
        }

        AppendSourceRange(source, sourceOffset, rawStart, source.Length - rawStart, text, sourceSpans);

        if (!changed)
            return Raw(source, sourceOffset);

        string presentedText = text.ToString();
        if (presentedText.Length > MarkdownViewerPresentationProvider.MaxPresentedLineChars)
            return Raw(source, sourceOffset);

        return new MarkdownInlineTransform(presentedText, sourceSpans, styleSpans, true);
    }

    private static MarkdownInlineTransform Raw(string source, int sourceOffset) =>
        new(
            source,
            source.Length == 0
                ? []
                : [new PresentedSourceSpan(sourceOffset, source.Length, 0, source.Length)],
            [],
            false);

    private static bool TryReadConstruct(
        string source,
        int index,
        IReadOnlyDictionary<int, int> codeClosings,
        out InlineConstruct construct)
    {
        construct = default;
        if (IsEscaped(source, index))
            return false;

        return source[index] switch
        {
            '`' => TryReadCode(source, index, codeClosings, out construct),
            '[' => TryReadLink(source, index, out construct),
            '*' => TryReadEmphasis(source, index, out construct),
            _ => false,
        };
    }

    private static bool TryReadCode(
        string source,
        int index,
        IReadOnlyDictionary<int, int> codeClosings,
        out InlineConstruct construct)
    {
        construct = default;
        if (index > 0 && source[index - 1] == '`')
            return false;

        int delimiterLength = CountRun(source, index, '`');
        if (!codeClosings.TryGetValue(index, out int closingStart))
            return false;

        int contentStart = index + delimiterLength;
        int contentLength = closingStart - contentStart;
        if (contentLength <= 0)
            return false;

        construct = new InlineConstruct(
            contentStart,
            contentLength,
            closingStart + delimiterLength,
            ViewerTextStyle.InlineCode);
        return true;
    }

    private static bool TryReadEmphasis(string source, int index, out InlineConstruct construct)
    {
        construct = default;
        if (index > 0 && source[index - 1] == '*')
            return false;

        int delimiterLength = CountRun(source, index, '*');
        if (delimiterLength is not (1 or 2))
            return false;

        int contentStart = index + delimiterLength;
        for (int current = contentStart; current < source.Length;)
        {
            if (source[current] != '*' || IsEscaped(source, current))
            {
                current++;
                continue;
            }

            int closingLength = CountRun(source, current, '*');
            if (closingLength == delimiterLength)
            {
                int contentLength = current - contentStart;
                if (contentLength <= 0)
                    return false;

                construct = new InlineConstruct(
                    contentStart,
                    contentLength,
                    current + closingLength,
                    delimiterLength == 2 ? ViewerTextStyle.Bold : ViewerTextStyle.Italic);
                return true;
            }

            current += closingLength;
        }

        return false;
    }

    private static bool TryReadLink(string source, int index, out InlineConstruct construct)
    {
        construct = default;
        if (index > 0 && source[index - 1] == '!')
            return false;

        int labelStart = index + 1;
        int closeBracket = -1;
        for (int current = labelStart; current < source.Length; current++)
        {
            if (source[current] == '[' && !IsEscaped(source, current))
                return false;

            if (source[current] == ']' && !IsEscaped(source, current))
            {
                closeBracket = current;
                break;
            }
        }

        if (closeBracket <= labelStart ||
            closeBracket + 1 >= source.Length ||
            source[closeBracket + 1] != '(' ||
            IsEscaped(source, closeBracket + 1))
        {
            return false;
        }

        int destinationStart = closeBracket + 2;
        if (destinationStart >= source.Length)
            return false;

        int closeParenthesis = -1;
        for (int current = destinationStart; current < source.Length; current++)
        {
            if (source[current] == '(' && !IsEscaped(source, current))
                return false;

            if (source[current] == ')' && !IsEscaped(source, current))
            {
                closeParenthesis = current;
                break;
            }
        }

        if (closeParenthesis <= destinationStart)
            return false;

        construct = new InlineConstruct(
            labelStart,
            closeBracket - labelStart,
            closeParenthesis + 1,
            ViewerTextStyle.Link);
        return true;
    }

    private static IReadOnlyDictionary<int, int> BuildCodeClosings(string source)
    {
        var nextByStart = new Dictionary<int, int>();
        var previousByLength = new Dictionary<int, int>();

        for (int index = 0; index < source.Length;)
        {
            if (source[index] != '`')
            {
                index++;
                continue;
            }

            int delimiterLength = CountRun(source, index, '`');
            if (!IsEscaped(source, index))
            {
                if (previousByLength.TryGetValue(delimiterLength, out int previousStart))
                    nextByStart[previousStart] = index;

                previousByLength[delimiterLength] = index;
            }

            index += delimiterLength;
        }

        return nextByStart;
    }

    private static void AppendSourceRange(
        string source,
        int sourceOffset,
        int sourceStart,
        int length,
        StringBuilder text,
        List<PresentedSourceSpan> sourceSpans)
    {
        if (length <= 0)
            return;

        int presentedStart = text.Length;
        text.Append(source, sourceStart, length);
        sourceSpans.Add(new PresentedSourceSpan(
            sourceOffset + sourceStart,
            length,
            presentedStart,
            length));
    }

    private static int CountRun(string source, int index, char value)
    {
        int length = 0;
        while (index + length < source.Length && source[index + length] == value)
            length++;
        return length;
    }

    private static bool IsEscaped(string source, int index)
    {
        int slashCount = 0;
        for (int current = index - 1; current >= 0 && source[current] == '\\'; current--)
            slashCount++;
        return slashCount % 2 != 0;
    }

    private readonly record struct InlineConstruct(
        int ContentStart,
        int ContentLength,
        int EndExclusive,
        ViewerTextStyle Style);
}
