using CSharpFar.Core.Models;

namespace CSharpFar.App.Editor;

internal static class EditorSettingsResolver
{
    public static EditorLineEnding ResolveDefaultLineEnding(AppSettings.EditorSettings settings) =>
        settings.DefaultLineEnding.Trim().ToUpperInvariant() switch
        {
            "CRLF" or "WINDOWS" or "DOS" => EditorLineEnding.CrLf,
            "CR" or "MAC" => EditorLineEnding.Cr,
            _ => EditorLineEnding.Lf,
        };

    public static int ResolveTabSize(AppSettings.EditorSettings settings) =>
        Math.Clamp(settings.TabSize, 1, 16);

    public static int ResolveUndoSize(AppSettings.EditorSettings settings) =>
        Math.Max(0, settings.UndoSize);

    public static int ResolveSyntaxMaxLineLength(AppSettings.EditorSettings settings) =>
        Math.Max(1, settings.SyntaxMaxLineLength);

    public static int ResolveSyntaxTokenizationTimeoutMs(AppSettings.EditorSettings settings) =>
        Math.Clamp(settings.SyntaxTokenizationTimeoutMs, 1, 1000);

    public static int ResolveSyntaxMaxSynchronousLines(AppSettings.EditorSettings settings) =>
        Math.Max(1, settings.SyntaxMaxSynchronousLines);

    public static string ResolveSyntaxLanguage(AppSettings.EditorSettings settings) =>
        string.IsNullOrWhiteSpace(settings.SyntaxLanguage)
            ? "auto"
            : settings.SyntaxLanguage.Trim();

    public static string ResolveSyntaxTheme(AppSettings.EditorSettings settings) =>
        string.IsNullOrWhiteSpace(settings.SyntaxTheme)
            ? "Dark+"
            : settings.SyntaxTheme.Trim();
}
