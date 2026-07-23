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
        ConsolePalette palette)
    {
        int innerWidth = bounds.Width - 2;
        if (innerWidth <= 0)
            return;

        bool touchesRightScreenEdge = bounds.Right == screen.Size.Width;
        int titleAreaWidth = Math.Max(0, innerWidth - (touchesRightScreenEdge ? ClockWidth : 0));
        int pathMaxLen = Math.Max(0, titleAreaWidth - 2);
        string label = FormatPathLabel(state.DisplayTitle ?? state.CurrentDirectory, pathMaxLen);
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
