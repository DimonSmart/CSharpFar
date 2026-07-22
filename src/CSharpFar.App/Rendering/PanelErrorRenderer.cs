using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using AppSettings = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Rendering;

internal static class PanelErrorRenderer
{
    private const string Heading = "Cannot read panel source";
    private const string RetryText = "[ Retry ]";

    public static void Render(
        IUiCanvas screen,
        Rect bounds,
        FilePanelState state,
        PanelViewMode mode,
        ConsolePalette palette,
        AppSettings.PanelOptionsSettings? options)
    {
        if (state.LoadError is null ||
            !TryGetContentRect(bounds, mode, options, out var content))
        {
            return;
        }

        var normalStyle = new CellStyle(palette.NormalFileFg, palette.PanelBackground);
        var errorStyle = new CellStyle(ConsoleColor.Yellow, palette.PanelBackground);
        var buttonStyle = new CellStyle(palette.CursorActiveFg, palette.CursorActiveBg);

        FillContent(screen, content, normalStyle);

        var layout = BuildLayout(content, state.LoadError.Message);
        if (layout.HeadingY.HasValue)
            WriteCentered(screen, content, layout.HeadingY.Value, Heading, errorStyle);

        for (int i = 0; i < layout.MessageRows.Count; i++)
            WriteCentered(screen, content, layout.MessageStartY + i, layout.MessageRows[i], errorStyle);

        if (layout.RetryButton.HasValue)
            screen.Write(
                layout.RetryButton.Value.X,
                layout.RetryButton.Value.Y,
                RetryText,
                buttonStyle);
    }

    public static bool HitTestRetry(
        int x,
        int y,
        Rect bounds,
        FilePanelState state,
        PanelViewMode mode,
        AppSettings.PanelOptionsSettings? options)
    {
        return TryGetRetryBounds(bounds, state, mode, options, out var retryButton) &&
            retryButton.Contains(x, y);
    }

    public static bool TryGetRetryBounds(
        Rect bounds,
        FilePanelState state,
        PanelViewMode mode,
        AppSettings.PanelOptionsSettings? options,
        out Rect retryBounds)
    {
        if (state.LoadError is null ||
            !TryGetContentRect(bounds, mode, options, out var content))
        {
            retryBounds = default;
            return false;
        }

        var layout = BuildLayout(content, state.LoadError.Message);
        if (layout.RetryButton is not { } retryButton)
        {
            retryBounds = default;
            return false;
        }

        retryBounds = retryButton;
        return true;
    }

    private static void FillContent(IUiCanvas screen, Rect content, CellStyle style)
    {
        string blank = new(' ', content.Width);
        for (int y = content.Y; y < content.Bottom; y++)
            screen.Write(content.X, y, blank, style);
    }

    private static PanelErrorLayout BuildLayout(Rect content, string message)
    {
        if (content.Height <= 0 || content.Width <= 0)
        {
            return new PanelErrorLayout(null, content.Y, [], null);
        }

        int messageWidth = Math.Max(0, content.Width - 2);
        int messageLimit = Math.Max(1, content.Height - 3);
        var messageRows = WrapMessage(message, messageWidth, messageLimit);

        bool showHeading = content.Height >= 3;
        bool showRetry = content.Height >= 2 && content.Width >= RetryText.Length;
        int blockHeight = messageRows.Count +
                          (showHeading ? 1 : 0) +
                          (showRetry ? 2 : 0);
        int startY = content.Y + Math.Max(0, (content.Height - blockHeight) / 2);

        int currentY = startY;
        int? headingY = null;
        if (showHeading)
            headingY = currentY++;

        int messageStartY = currentY;
        currentY += messageRows.Count;

        Rect? retryButton = null;
        if (showRetry)
        {
            currentY++;
            int retryX = content.X + Math.Max(0, (content.Width - RetryText.Length) / 2);
            retryButton = new Rect(retryX, Math.Min(content.Bottom - 1, currentY), RetryText.Length, 1);
        }

        return new PanelErrorLayout(headingY, messageStartY, messageRows, retryButton);
    }

    private static List<string> WrapMessage(string message, int width, int maxRows)
    {
        if (width <= 0 || maxRows <= 0)
            return [];

        var rows = new List<string>();
        foreach (string sourceLine in message.Replace("\r", string.Empty).Split('\n'))
        {
            string remaining = sourceLine.Trim();
            if (remaining.Length == 0)
            {
                AddRow(rows, string.Empty, maxRows);
                if (rows.Count >= maxRows)
                    break;
                continue;
            }

            while (remaining.Length > width)
            {
                int split = remaining.LastIndexOf(' ', width);
                if (split <= 0)
                    split = width;

                AddRow(rows, remaining[..split].TrimEnd(), maxRows);
                remaining = remaining[split..].TrimStart();
                if (rows.Count >= maxRows)
                    break;
            }

            if (rows.Count >= maxRows)
                break;

            AddRow(rows, remaining, maxRows);
            if (rows.Count >= maxRows)
                break;
        }

        return rows.Count == 0 ? ["Error"] : rows;
    }

    private static void AddRow(List<string> rows, string row, int maxRows)
    {
        if (rows.Count >= maxRows)
            return;

        rows.Add(row);
    }

    private static bool TryGetContentRect(
        Rect bounds,
        PanelViewMode mode,
        AppSettings.PanelOptionsSettings? options,
        out Rect content)
    {
        int top = bounds.Y + (mode == PanelViewMode.BriefTwoColumns ? 2 : 1);
        int statusRows = PanelStatusRenderer.GetStatusRowCount(options);
        int bottom = bounds.Bottom - 1 - statusRows;
        int height = Math.Max(0, bottom - top);
        int width = Math.Max(0, bounds.Width - 2);
        content = new Rect(bounds.X + 1, top, width, height);
        return width > 0 && height > 0;
    }

    private static void WriteCentered(
        IUiCanvas screen,
        Rect content,
        int y,
        string text,
        CellStyle style)
    {
        string fitted = PanelStatusRenderer.Truncate(text, content.Width);
        int x = content.X + Math.Max(0, (content.Width - fitted.Length) / 2);
        screen.Write(x, y, fitted, style);
    }

    private sealed record PanelErrorLayout(
        int? HeadingY,
        int MessageStartY,
        List<string> MessageRows,
        Rect? RetryButton);
}
