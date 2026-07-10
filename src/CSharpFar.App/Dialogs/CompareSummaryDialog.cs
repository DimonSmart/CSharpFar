using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Core.Comparison;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal static class CompareSummaryDialog
{
    public static void Show(ScreenRenderer screen, CompareResult result)
    {
        CompareSummary summary = result.Summary;
        if (summary.DifferentCount + summary.LeftOnlyCount + summary.RightOnlyCount + summary.AmbiguousCount + summary.ErrorCount == 0)
        {
            new MessageDialog(screen).Show("Compare", "No differences found.");
            return;
        }

        var lines = new List<string> { $"Matched: {summary.EqualCount}" };
        if (summary.LeftOnlyCount > 0)
            lines.Add($"Left only: {summary.LeftOnlyCount} ({FormatSize(Size(result.Rows, CompareStatus.LeftOnly, left: true))})");
        if (summary.RightOnlyCount > 0)
            lines.Add($"Right only: {summary.RightOnlyCount} ({FormatSize(Size(result.Rows, CompareStatus.RightOnly, left: false))})");
        if (summary.DifferentCount > 0)
        {
            long left = Size(result.Rows, CompareStatus.Different, left: true);
            long right = Size(result.Rows, CompareStatus.Different, left: false);
            lines.Add($"Different: {summary.DifferentCount} (left {FormatSize(left)}, right {FormatSize(right)})");
        }
        if (summary.AmbiguousCount > 0)
            lines.Add($"Ambiguous or duplicate: {summary.AmbiguousCount} groups");
        if (summary.ErrorCount > 0)
            lines.Add($"Errors: {summary.ErrorCount}");

        new MessageDialog(screen).Show("Compare", string.Join(Environment.NewLine, lines));
    }

    private static long Size(IEnumerable<CompareResultRow> rows, CompareStatus status, bool left) =>
        rows.Where(row => row.Status == status)
            .SelectMany(row => left ? row.LeftEntries : row.RightEntries)
            .Sum(entry => entry.Size ?? 0);

    private static string FormatSize(long bytes) => PanelStatusRenderer.FormatSize(bytes);
}
