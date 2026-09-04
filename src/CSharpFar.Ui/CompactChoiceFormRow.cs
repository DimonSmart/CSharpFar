using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>A one-line labeled choice that keeps the compact FTP-style presentation.</summary>
public sealed class CompactChoiceFormRow<T> : FormRow, IFormFocusTarget, IFormCursorProvider, IFormMnemonic
{
    private readonly ChoiceModel<T> _choice;
    private readonly string _label;

    internal CompactChoiceFormRow(ChoiceModel<T> choice, string label)
    {
        _choice = choice ?? throw new ArgumentNullException(nameof(choice));
        FormLabel parsed = FormLabelParser.Parse(label);
        _label = parsed.Text;
        Mnemonic = parsed.Mnemonic;
    }

    internal CompactChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer = null)
        : this(new ChoiceModel<T>(ChoiceSelection<T>.FromValue(values, selectedValue, comparer), format), label)
    {
    }

    internal CompactChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        IEqualityComparer<T>? comparer = null)
        : this(new ChoiceModel<T>(ChoiceSelection<T>.FromValueOrFallback(values, selectedValue, fallbackValue, comparer), format), label)
    {
    }

    internal override FormRowRole Role { get; init; } = FormRowRole.Option;
    internal ChoiceModel<T> Choice => _choice;
    public T Value { get => _choice.Value; set => _choice.Value = value; }
    public bool Enabled { get; set; } = true;
    public string? DisabledReason { get; set; }
    internal override bool IsEnabled => Enabled;
    char? IFormMnemonic.Mnemonic => Mnemonic;
    private char? Mnemonic { get; }
    internal override int DesiredWidth =>
        ConsoleTextMetrics.GetCellWidth(_label) + 2 +
        _choice.Selection.Items.Max(item => ConsoleTextMetrics.GetCellWidth(_choice.Format(item)));

    internal override void Render(FormRowRenderContext context) => ChoiceRenderer.Render(context.Canvas,
        ChoiceLayoutCalculator.Compact(context.Bounds), _choice.Selection, _choice.Format,
        !Enabled ? DisabledFormControlPresentation.WithReason(_label, DisabledReason) : _label,
        new(DisabledFormControlPresentation.Style(Enabled, DialogStyles.Fill), DisabledFormControlPresentation.Style(Enabled, DialogStyles.FocusedInput), context.Focused && Enabled));

    bool IFormCursorProvider.TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
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

    internal override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        return ToFormResult(ChoiceInput.HandleKey(_choice.Selection, key));
    }

    internal override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        return ToFormResult(ChoiceInput.HandleMouse(_choice.Selection, mouse, ChoiceLayoutCalculator.Compact(context.Bounds)));
    }

    private static FormInputResult ToFormResult(ChoiceInputResultKind result) => result switch { ChoiceInputResultKind.Handled => FormInputResult.Handled, ChoiceInputResultKind.ValueChanged => FormInputResult.ValueChanged, _ => FormInputResult.NotHandled };
}
