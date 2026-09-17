namespace CSharpFar.App.Viewer;

internal enum MarkdownLinkTargetKind
{
    Unsupported,
    ExternalUri,
    RelativeFile,
}

internal sealed record MarkdownLinkTarget(
    MarkdownLinkTargetKind Kind,
    Uri? Uri = null,
    string? FilePath = null)
{
    public static MarkdownLinkTarget Unsupported { get; } = new(MarkdownLinkTargetKind.Unsupported);
}

internal static class MarkdownLinkTargetResolver
{
    public static MarkdownLinkTarget Resolve(string? target, string? sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(target) || target.StartsWith('#'))
            return MarkdownLinkTarget.Unsupported;

        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
        {
            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return new MarkdownLinkTarget(MarkdownLinkTargetKind.ExternalUri, Uri: uri);
            }

            return MarkdownLinkTarget.Unsupported;
        }

        if (LooksLikeUriScheme(target) ||
            target.Contains('?') ||
            target.Contains('#') ||
            IsRootedTarget(target))
        {
            return MarkdownLinkTarget.Unsupported;
        }

        if (string.IsNullOrWhiteSpace(sourceFilePath) || !Path.IsPathRooted(sourceFilePath))
            return MarkdownLinkTarget.Unsupported;

        try
        {
            string fullSourcePath = Path.GetFullPath(sourceFilePath);
            string? baseDirectory = Path.GetDirectoryName(fullSourcePath);
            if (string.IsNullOrEmpty(baseDirectory))
                return MarkdownLinkTarget.Unsupported;

            string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, target));
            if (Directory.Exists(fullPath))
                return MarkdownLinkTarget.Unsupported;

            return new MarkdownLinkTarget(MarkdownLinkTargetKind.RelativeFile, FilePath: fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return MarkdownLinkTarget.Unsupported;
        }
    }

    private static bool LooksLikeUriScheme(string target)
    {
        int colon = target.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(target[0]))
            return false;

        for (int i = 1; i < colon; i++)
        {
            char ch = target[i];
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not '+' and not '-' and not '.')
                return false;
        }

        return true;
    }

    private static bool IsRootedTarget(string target) =>
        Path.IsPathRooted(target) ||
        target.StartsWith('/') ||
        target.StartsWith('\') ||
        (target.Length >= 2 && char.IsAsciiLetter(target[0]) && target[1] == ':');
}
