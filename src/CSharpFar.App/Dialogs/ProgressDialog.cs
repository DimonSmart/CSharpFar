using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>Draws Far-like modal progress overlays during file operations.</summary>
internal sealed class ProgressDialog
{
    private const int ScanOuterWidth = 68;
    private const int ScanOuterHeight = 12;
    private const int CopyOuterWidth = 74;
    private const int CopyOuterHeight = 18;
    private const int DeleteOuterWidth = 68;
    private const int DeleteOuterHeight = 12;

    private readonly IUiCanvas _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly string _destination;

    public ProgressDialog(IUiCanvas screen, string destination)
    {
        _screen = screen;
        _destination = destination;
    }

    public void Render(UiRenderContext context, FileOperationProgress progress, bool showTotalProgress)
    {
        if (progress.Phase == FileOperationPhase.Scanning)
            RenderScanning(context, progress);
        else if (progress.Kind == FileOperationKind.Delete || progress.Phase == FileOperationPhase.Deleting)
            RenderDeleting(context, progress);
        else
            RenderCopying(context, progress, showTotalProgress);
    }

    private void RenderScanning(UiRenderContext context, FileOperationProgress progress)
    {
        var outer = _modalRenderer.CenteredOuterBounds(context.Size, ScanOuterWidth, ScanOuterHeight);
        RenderOuter(outer, "Copy", true, (frameBounds, contentX, contentWidth) =>
        {
            _screen.Write(contentX, frameBounds.Y + 1, "Scanning the folder".PadRight(contentWidth), FillStyle);
            _screen.Write(contentX, frameBounds.Y + 2, ShortenMiddle(DisplayFolderName(progress.CurrentPath), contentWidth).PadRight(contentWidth), FillStyle);

            DrawSeparator(frameBounds, frameBounds.Y + 4);

            DrawCounter(contentX, frameBounds.Y + 5, contentWidth, "Files:", FormatInteger(progress.ItemsDone));
            DrawCounter(contentX, frameBounds.Y + 6, contentWidth, "Folders:", FormatInteger(progress.FoldersDone));
            DrawCounter(contentX, frameBounds.Y + 7, contentWidth, "Bytes:", FormatInteger(progress.TotalBytesDone));
        });
    }

    private void RenderCopying(UiRenderContext context, FileOperationProgress progress, bool showTotalProgress)
    {
        var outer = _modalRenderer.CenteredOuterBounds(context.Size, CopyOuterWidth, CopyOuterHeight);
        RenderOuter(outer, "Copy", true, (frameBounds, contentX, contentWidth) =>
        {
            string status = progress.Phase == FileOperationPhase.Validating
                ? progress.StatusMessage ?? "Validating partial file..."
                : "Copying the file";
            _screen.Write(contentX, frameBounds.Y + 1, ShortenMiddle(status, contentWidth).PadRight(contentWidth), FillStyle);
            _screen.Write(contentX, frameBounds.Y + 2, ShortenMiddle(progress.CurrentPath, contentWidth).PadRight(contentWidth), FillStyle);
            _screen.Write(contentX, frameBounds.Y + 3, "to".PadRight(contentWidth), FillStyle);
            string destination = progress.CurrentDestinationPath ?? _destination;
            _screen.Write(contentX, frameBounds.Y + 4, ShortenMiddle(destination, contentWidth).PadRight(contentWidth), FillStyle);
            DrawProgressBar(contentX, frameBounds.Y + 5, contentWidth, progress.CurrentBytesDone, progress.CurrentBytesTotal);
            DrawResumeLine(contentX, frameBounds.Y + 6, contentWidth, progress);

            DrawTitledSeparator(frameBounds, frameBounds.Y + 7, "Total");
            DrawCounter(contentX, frameBounds.Y + 8, contentWidth, "Files:", $"{FormatInteger(progress.ItemsDone)} / {FormatInteger(progress.ItemsTotal)}");
            DrawCounter(contentX, frameBounds.Y + 9, contentWidth, "Bytes:", $"{FormatInteger(progress.TotalBytesDone)} / {FormatInteger(progress.TotalBytesTotal)}");

            if (showTotalProgress)
                DrawProgressBar(contentX, frameBounds.Y + 10, contentWidth, progress.TotalBytesDone, progress.TotalBytesTotal);

            DrawSeparator(frameBounds, frameBounds.Y + 12);
            DrawTimeLine(contentX, frameBounds.Y + 13, contentWidth, progress);
        });
    }

    private void RenderDeleting(UiRenderContext context, FileOperationProgress progress)
    {
        var outer = _modalRenderer.CenteredOuterBounds(context.Size, DeleteOuterWidth, DeleteOuterHeight);
        RenderOuter(outer, "Delete", true, (frameBounds, contentX, contentWidth) =>
        {
            string status = progress.StatusMessage ?? "Deleting the file";
            _screen.Write(contentX, frameBounds.Y + 1, ShortenMiddle(status, contentWidth).PadRight(contentWidth), FillStyle);
            _screen.Write(contentX, frameBounds.Y + 2, ShortenMiddle(progress.CurrentPath, contentWidth).PadRight(contentWidth), FillStyle);

            DrawTitledSeparator(frameBounds, frameBounds.Y + 4, "Total");
            DrawCounter(contentX, frameBounds.Y + 5, contentWidth, "Files:", $"{FormatInteger(progress.ItemsDone)} / {FormatInteger(progress.ItemsTotal)}");
            DrawCounter(contentX, frameBounds.Y + 6, contentWidth, "Bytes:", $"{FormatInteger(progress.TotalBytesDone)} / {FormatInteger(progress.TotalBytesTotal)}");

            DrawSeparator(frameBounds, frameBounds.Y + 8);
            DrawTimeLine(contentX, frameBounds.Y + 9, contentWidth, progress);
        });
    }

    private void DrawResumeLine(int x, int y, int width, FileOperationProgress progress)
    {
        string text = string.Empty;
        if (progress.ResumeOffset.HasValue)
        {
            text = $"Resume offset: {FormatInteger(progress.ResumeOffset.Value)}";
            long rollbackBytes = progress.ResumeRollbackBytes.GetValueOrDefault();
            if (rollbackBytes > 0)
                text += $"  Rollback: {FormatInteger(rollbackBytes)}";
        }
        else if (progress.Phase == FileOperationPhase.Validating)
        {
            text = "Resume offset: validating";
        }

        _screen.Write(x, y, ShortenMiddle(text, width).PadRight(width), FillStyle);
    }

    private void RenderOuter(Rect outer, string title, bool doubleBorder, Action<Rect, int, int> renderContent)
    {
        _modalRenderer.Render(
            _screen,
            outer,
            title,
            doubleBorder,
            OuterPopupOptions,
            InnerPopupOptions,
            (_, layout) =>
            {
                Rect frameBounds = layout.FrameBounds;
                int contentX = frameBounds.X + 2;
                int contentWidth = Math.Max(1, frameBounds.Width - 4);
                renderContent(frameBounds, contentX, contentWidth);
            });
    }

    private void DrawCounter(int x, int y, int width, string label, string value)
    {
        int valueWidth = Math.Max(0, width - label.Length);
        _screen.Write(x, y, label, FillStyle);
        _screen.Write(x + label.Length, y, ShortenLeft(value, valueWidth).PadLeft(valueWidth), FillStyle);
    }

    private void DrawProgressBar(int x, int y, int width, long value, long total)
    {
        string percent = PercentText(value, total);
        int barWidth = Math.Max(1, width - percent.Length - 2);
        int filled = total <= 0 ? 0 : (int)Math.Clamp(value * barWidth / total, 0, barWidth);

        string bar = new string('█', filled) + new string('░', barWidth - filled);
        _screen.Write(x, y, bar, FillStyle);
        _screen.Write(x + barWidth + 2, y, percent, FillStyle);
    }

    private void DrawTimeLine(int x, int y, int width, FileOperationProgress progress)
    {
        string left = $"Time: {FormatTime(progress.Elapsed)}";
        string middle = $"Remaining: {FormatTime(progress.TimeRemaining ?? TimeSpan.Zero)}";
        string right = FormatSpeed(progress.BytesPerSecond);

        var line = new char[width];
        Array.Fill(line, ' ');
        WriteInto(line, 0, left);
        WriteInto(line, Math.Max(0, (width - middle.Length) / 2), middle);
        WriteInto(line, Math.Max(0, width - right.Length), right);
        _screen.Write(x, y, new string(line), FillStyle);
    }

    private void DrawSeparator(Rect inner, int y)
    {
        if (y <= inner.Y || y >= inner.Bottom - 1)
            return;

        char left = '╟';
        char line = '─';
        char right = '╢';

        _screen.WriteChar(inner.X, y, left, BorderStyle);
        _screen.Write(inner.X + 1, y, new string(line, Math.Max(0, inner.Width - 2)), BorderStyle);
        _screen.WriteChar(inner.Right - 1, y, right, BorderStyle);
    }

    private void DrawTitledSeparator(Rect inner, int y, string title)
    {
        if (y <= inner.Y || y >= inner.Bottom - 1)
            return;

        string titleText = $" {title} ";
        int lineWidth = Math.Max(0, inner.Width - 2);
        int leftWidth = Math.Max(0, (lineWidth - titleText.Length) / 2);
        int rightWidth = Math.Max(0, lineWidth - leftWidth - titleText.Length);
        string line = new string('─', leftWidth) + titleText + new string('─', rightWidth);

        _screen.WriteChar(inner.X, y, '╟', BorderStyle);
        _screen.Write(inner.X + 1, y, line, BorderStyle);
        _screen.WriteChar(inner.Right - 1, y, '╢', BorderStyle);
    }

    private static void WriteInto(char[] target, int start, string value)
    {
        if (start >= target.Length)
            return;

        for (int i = 0; i < value.Length && start + i < target.Length; i++)
            target[start + i] = value[i];
    }

    private static string PercentText(long value, long total)
    {
        long percent = total <= 0 ? 0 : Math.Clamp(value * 100 / total, 0, 100);
        return $"{percent,3}%";
    }

    private static string FormatInteger(long value) =>
        value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

    private static string FormatTime(TimeSpan value) =>
        value.ToString(@"hh\:mm\:ss");

    private static string FormatSpeed(double bytesPerSecond)
    {
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        double value = Math.Max(0, bytesPerSecond);
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:0}{units[unitIndex]}"
            : $"{value:0.0}{units[unitIndex]}";
    }

    private static string ShortenMiddle(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        if (value.Length <= maxLength)
            return value;
        if (maxLength <= 1)
            return "…";

        int left = (maxLength - 1) / 2;
        int right = maxLength - left - 1;
        return value[..left] + "…" + value[^right..];
    }

    private static string ShortenLeft(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : "…" + value[^Math.Max(0, maxLength - 1)..];
    }

    private static string DisplayFolderName(string path)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static CellStyle FillStyle => new(ConsoleColor.Black, ConsoleColor.Gray);
    private static CellStyle BorderStyle => new(ConsoleColor.DarkGray, ConsoleColor.Gray);
    private static CellStyle TitleStyle => new(ConsoleColor.Black, ConsoleColor.Gray);
    private static CellStyle ShadowStyle => new(ConsoleColor.Black, ConsoleColor.Black);

    private static PopupRenderOptions OuterPopupOptions =>
        new()
        {
            DrawBorder = false,
            BorderStyle = BorderStyle,
            BackgroundStyle = FillStyle,
            ShadowStyle = ShadowStyle,
            TitleStyle = TitleStyle,
        };

    private static PopupRenderOptions InnerPopupOptions =>
        new()
        {
            DrawShadow = false,
            BorderStyle = BorderStyle,
            BackgroundStyle = FillStyle,
            ShadowStyle = ShadowStyle,
            TitleStyle = TitleStyle,
        };
}
