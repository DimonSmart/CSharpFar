using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

internal static class DisabledFormControlPresentation
{
    public static CellStyle Style(bool enabled, CellStyle normalStyle) =>
        enabled ? normalStyle : DialogStyles.DisabledControl(normalStyle);

    public static string WithReason(string text, string? disabledReason) =>
        string.IsNullOrWhiteSpace(disabledReason) ? text : $"{text} - {disabledReason}";
}
