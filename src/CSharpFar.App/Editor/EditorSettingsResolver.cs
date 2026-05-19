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
}
