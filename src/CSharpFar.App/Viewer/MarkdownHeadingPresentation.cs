namespace CSharpFar.App.Viewer;

internal static class MarkdownHeadingPresentation
{
    public static bool TryTransform(ScannedLine source, out PresentedLine presented)
    {
        presented = PresentedLine.Raw(source);
        string text = source.Text;
        if (text.Length > MarkdownViewerPresentationProvider.MaxPresentedLineChars)
            return false;

        int markerStart = 0;
        while (markerStart < text.Length && markerStart < 4 && text[markerStart] == ' ')
            markerStart++;

        if (markerStart >= 4 || markerStart >= text.Length || text[markerStart] != '#')
            return false;

        int markerEnd = markerStart;
        while (markerEnd < text.Length && text[markerEnd] == '#')
            markerEnd++;

        int level = markerEnd - markerStart;
        if (level is < 1 or > 6)
            return false;

        if (markerEnd < text.Length && !IsSpaceOrTab(text[markerEnd]))
            return false;

        int contentStart = markerEnd;
        while (contentStart < text.Length && IsSpaceOrTab(text[contentStart]))
            contentStart++;

        int contentEnd = text.Length;
        while (contentEnd > markerEnd && IsSpaceOrTab(text[contentEnd - 1]))
            contentEnd--;
        contentEnd = Math.Max(contentEnd, contentStart);

        if (contentEnd > contentStart && text[contentEnd - 1] == '#')
        {
            int closingStart = contentEnd - 1;
            while (closingStart > contentStart && text[closingStart - 1] == '#')
                closingStart--;

            if (closingStart > 0 &&
                IsSpaceOrTab(text[closingStart - 1]) &&
                !IsEscaped(text, closingStart))
            {
                contentEnd = closingStart;
                while (contentEnd > contentStart && IsSpaceOrTab(text[contentEnd - 1]))
                    contentEnd--;
            }
        }

        string content = text[contentStart..contentEnd];
        MarkdownInlineTransform inline = MarkdownInlinePresentation.Transform(content, contentStart);
        if (inline.Text.Length > MarkdownViewerPresentationProvider.MaxPresentedLineChars ||
            !TryBuildStyles(inline, HeadingStyle(level), out var styles))
        {
            return false;
        }

        presented = new PresentedLine(source, inline.Text, inline.SourceSpans, styles);
        return true;
    }

    private static bool TryBuildStyles(
        MarkdownInlineTransform inline,
        ViewerTextStyle headingStyle,
        out IReadOnlyList<PresentedStyleSpan> styles)
    {
        if (inline.Text.Length == 0)
        {
            styles = [];
            return inline.StyleSpans.Count == 0;
        }

        var result = new List<PresentedStyleSpan>(inline.StyleSpans.Count * 2 + 1);
        int next = 0;
        foreach (PresentedStyleSpan span in inline.StyleSpans)
        {
            if (span.Length <= 0 || span.Start < next || span.Start + span.Length > inline.Text.Length)
            {
                styles = [];
                return false;
            }

            if (span.Start > next)
                result.Add(new PresentedStyleSpan(next, span.Start - next, headingStyle));

            result.Add(span);
            next = span.Start + span.Length;
        }

        if (next < inline.Text.Length)
            result.Add(new PresentedStyleSpan(next, inline.Text.Length - next, headingStyle));

        styles = result;
        return true;
    }

    private static ViewerTextStyle HeadingStyle(int level) =>
        level switch
        {
            1 => ViewerTextStyle.Heading1,
            2 => ViewerTextStyle.Heading2,
            3 => ViewerTextStyle.Heading3,
            4 => ViewerTextStyle.Heading4,
            5 => ViewerTextStyle.Heading5,
            6 => ViewerTextStyle.Heading6,
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };

    private static bool IsSpaceOrTab(char value) => value is ' ' or '\t';

    private static bool IsEscaped(string source, int index)
    {
        int slashCount = 0;
        for (int current = index - 1; current >= 0 && source[current] == '\\'; current--)
            slashCount++;
        return slashCount % 2 != 0;
    }
}
