using System.Text.RegularExpressions;

namespace CSharpFar.App.Diagnostics;

internal static class DiagnosticSanitizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex UrlRegex = new(
        """https?://[^\s<>"']+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex AuthorizationRegex = new(
        """\bAuthorization\s*[:=]\s*[^\r\n]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex BearerRegex = new(
        """\bBearer\s+[A-Za-z0-9._~+/=-]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex SecretAssignmentRegex = new(
        """\b(password|passwd|pwd|api[-_ ]?key|access[-_ ]?token|token)\s*[:=]\s*[^;\s,]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    public static string SanitizeText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        try
        {
            string sanitized = UrlRegex.Replace(text, static match => SanitizeUrl(match.Value));
            sanitized = AuthorizationRegex.Replace(sanitized, "Authorization: <redacted>");
            sanitized = BearerRegex.Replace(sanitized, "Bearer <redacted>");
            sanitized = SecretAssignmentRegex.Replace(
                sanitized,
                static match => $"{match.Groups[1].Value}=<redacted>");
            return RedactHomeInText(sanitized);
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or UriFormatException or ArgumentException)
        {
            return "[redacted: diagnostic text unavailable]";
        }
    }

    public static string SanitizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "[redacted-url]";
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.GetLeftPart(UriPartial.Path);
    }

    public static string RedactHomePath(string path, string? homeDirectory = null)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        homeDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(homeDirectory))
            return path;

        string home = homeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(path, home, comparison))
            return "~";

        if (path.Length > home.Length &&
            path.StartsWith(home, comparison) &&
            (path[home.Length] == Path.DirectorySeparatorChar ||
             path[home.Length] == Path.AltDirectorySeparatorChar))
        {
            return "~" + path[home.Length..];
        }

        return path;
    }

    private static string RedactHomeInText(string text)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            return text;

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return text.Replace(home, "~", comparison);
    }
}
