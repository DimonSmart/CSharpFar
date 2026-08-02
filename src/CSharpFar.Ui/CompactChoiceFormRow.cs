using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>A one-line labeled choice that keeps the compact FTP-style presentation.</summary>
public sealed class CompactChoiceFormRow<T> : FormRow, IFormCursorProvider
{
    private readonly ChoiceRow<T> _choice;
    private readonly string _label;

    public CompactChoiceFormRow(ChoiceRow<T> choice, string label)
    {
        _choice = choice ?? throw new ArgumentNullException(nameof(choice));
        _label = label;
    }

    public CompactChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer = null)
        : this(ChoiceRow<T>.FromValue(values, format, selectedValue, comparer), label)
    {
    }

    public CompactChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        IEqualityComparer<T>? comparer = null)
        : this(ChoiceRow<T>.FromValue(values, format, selectedValue, fallbackValue, comparer), label)
    {
    }

    public override FormRowRole Role { get; init; } = FormRowRole.Option;
    public ChoiceRow<T> Choice => _choice;
    public T Value => _choice.Value;
    public bool Enabled { get; set; } = true;
    public string? DisabledReason { get; set; }
    public override bool IsEnabled => Enabled;

    public override void Render(FormRowRenderContext context) => ChoiceRenderer.Render(context.Canvas,
        ChoiceLayoutCalculator.Compact(context.Bounds), _choice.Selection, _choice.Format,
        !Enabled ? DisabledFormControlPresentation.WithReason(_label, DisabledReason) : _label,
        new(DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.Fill), DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.FocusedInput), context.Focused && Enabled));

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        if (!Enabled || !context.Focused || context.Bounds.Width <= 0)
        {
            cursor = default;
            return false;
        }

        int valueOffset = ConsoleTextMetrics.GetCellWidth(_label) + 2;
        cursor = new FormCursorPlacement(context.Bounds.X + Math.Min(context.Bounds.Width - 1, valueOffset), context.Bounds.Y);
        return true;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        return ToFormResult(ChoiceInput.HandleKey(_choice.Selection, key));
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        return ToFormResult(ChoiceInput.HandleMouse(_choice.Selection, mouse, ChoiceLayoutCalculator.Compact(context.Bounds)));
    }

    private static FormInputResult ToFormResult(ChoiceInputResultKind result) => result switch { ChoiceInputResultKind.Handled => FormInputResult.Handled, ChoiceInputResultKind.ValueChanged => FormInputResult.ValueChanged, _ => FormInputResult.NotHandled };
}
