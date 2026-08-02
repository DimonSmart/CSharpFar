using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public enum FormLabelColumnMode
{
    Auto,
    PerRow,
    Fixed,
}

public enum FormCursorPolicy
{
    ControlDefault,
    TextInputsOnly,
    Hidden,
}

public sealed record FormLayoutOptions(
    FormLabelColumnMode LabelColumnMode = FormLabelColumnMode.Auto,
    int LabelGap = 1,
    int? FixedLabelWidth = null,
    int MinimumControlWidth = 8,
    FormCursorPolicy CursorPolicy = FormCursorPolicy.ControlDefault)
{
    public FormLayoutOptions Validate()
    {
        if (LabelGap < 0)
            throw new ArgumentOutOfRangeException(nameof(LabelGap));
        if (FixedLabelWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(FixedLabelWidth));
        if (MinimumControlWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumControlWidth));
        if (LabelColumnMode == FormLabelColumnMode.Fixed && FixedLabelWidth is null)
            throw new ArgumentException("A fixed label-column mode requires a fixed label width.", nameof(FixedLabelWidth));
        return this;
    }
}

internal interface IFormLabeledRow
{
    int DesiredLabelWidth { get; }
    bool UseSharedLabelColumn { get; }
}

public readonly record struct FormRowLayout(Rect RowBounds, Rect? LabelBounds, Rect ControlBounds);
