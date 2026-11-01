using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal static class PanelTitleRenderer
{
    private const int ClockWidth = 5;

    public static void Render(
        IUiCanvas screen,
        Rect bounds,
        FilePanelState state,
        bool isActive,
        CSharpFarPalette palette)
    {
        int innerWidth = bounds.Width - 2;
        if (innerWidth <= 0)
            return;

        bool touchesRightScreenEdge = bounds.Right == screen.Size.Width;
        int titleAreaWidth = Math.Max(0, innerWidth - (touchesRightScreenEdge ? ClockWidth : 0));
        int pathMaxLen = Math.Max(0, titleAreaWidth - 2);
        string label = FormatLabel(state, pathMaxLen);
        if (label.Length == 0)
            return;

        string text = $" {label} ";
        int titleX = bounds.X + 1 + Math.Max(0, (titleAreaWidth - text.Length) / 2);

        var textStyle = new CellStyle(
            palette.PanelTitleFocusedFg,
            palette.PanelBackground);
        var activePathStyle = new CellStyle(palette.PanelPathActiveFg, palette.PanelPathActiveBg);

        screen.Write(titleX, bounds.Y, text, isActive ? activePathStyle : textStyle);
    }


    internal static string FormatLabel(FilePanelState state, int maxLen)
    {
        if (maxLen <= 0)
            return string.Empty;

        if (string.IsNullOrEmpty(state.DisplayTitle))
            return FormatPathLabel(state.CurrentDirectory, maxLen);

        string title = state.DisplayTitle;
        string suffix = GetSourcePathSuffix(state.SourcePath, state.DisplayTitleBasePath);
        if (suffix.Length == 0)
            return FormatSourceTitle(title, maxLen);

        if (title.Length + suffix.Length <= maxLen)
            return title + suffix;

        int markerEnd = title.IndexOf(':');
        int minimumTitleWidth = markerEnd >= 0
            ? Math.Min(maxLen, markerEnd + 2)
            : 1;
        int lastSeparator = Math.Max(suffix.LastIndexOf('/'), suffix.LastIndexOf('\\'));
        int currentFolderWidth = suffix.Length - lastSeparator - 1;
        int preferredSuffixWidth = Math.Min(suffix.Length, Math.Max(maxLen / 3, currentFolderWidth + 3));
        int suffixWidth = Math.Min(maxLen - minimumTitleWidth, Math.Max(preferredSuffixWidth, maxLen - title.Length));
        return FormatSourceTitle(title, maxLen - suffixWidth) + FormatSourcePath(suffix, suffixWidth);
    }

    private static string GetSourcePathSuffix(string currentPath, string? basePath)
    {
        if (string.IsNullOrEmpty(basePath) || currentPath == basePath)
            return string.Empty;

        if (currentPath.StartsWith(basePath, StringComparison.Ordinal))
        {
            string relative = currentPath[basePath.Length..];
            if (basePath.EndsWith('/') || basePath.EndsWith('\\') ||
                relative.StartsWith('/') || relative.StartsWith('\\'))
            {
                return relative.StartsWith('/') || relative.StartsWith('\\')
                    ? relative
                    : "/" + relative;
            }
        }

        return currentPath.StartsWith('/') || currentPath.StartsWith('\\')
            ? currentPath
            : "/" + currentPath;
    }

    private static string FormatSourceTitle(string title, int maxLen)
    {
        if (maxLen <= 0)
            return string.Empty;
        if (title.Length <= maxLen)
            return title;

        int markerEnd = title.IndexOf(':');
        if (markerEnd < 0 || markerEnd + 1 >= maxLen)
            return title[..maxLen];

        string marker = title[..(markerEnd + 1)];
        return marker + TruncateLeft(title[marker.Length..], maxLen - marker.Length);
    }

    private static string FormatSourcePath(string suffix, int maxLen)
    {
        if (maxLen <= 0)
            return string.Empty;
        if (suffix.Length <= maxLen)
            return suffix;

        if (suffix[0] is '/' or '\\')
            return maxLen == 1 ? suffix[..1] : suffix[..1] + TruncateLeft(suffix[1..], maxLen - 1);

        return TruncateLeft(suffix, maxLen);
    }

    private static string FormatPathLabel(string path, int maxLen)
    {
        if (maxLen <= 0)
            return string.Empty;

        int driveLength = GetDriveNameLength(path);
        if (path.Length <= maxLen)
            return path;

        if (driveLength <= 0)
            return TruncateLeft(path, maxLen);

        int visibleDriveLength = Math.Min(driveLength, maxLen);
        if (visibleDriveLength >= maxLen)
            return path[..visibleDriveLength];

        string drive = path[..visibleDriveLength];
        string tail = TruncateLeft(path[driveLength..], maxLen - visibleDriveLength);
        return drive + tail;
    }

    private static int GetDriveNameLength(string path)
    {
        if (path.Length >= 2 && path[1] == ':')
            return 2;

        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            return 0;

        string trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length > 0 ? Math.Min(trimmed.Length, path.Length) : root.Length;
    }

    private static string TruncateLeft(string text, int maxLen)
    {
        if (maxLen <= 0)
            return string.Empty;
        if (text.Length <= maxLen)
            return text;
        if (maxLen == 1)
            return "\u2026";
        return "\u2026" + text[^(maxLen - 1)..];
    }
}
