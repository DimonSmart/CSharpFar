using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Ui;

public sealed class ChoiceFormRow<T> : FormRow, IFormFocusTarget, IFormCursorProvider, IFormMnemonic
{
    internal override FormRowRole Role { get; init; } = FormRowRole.Option;

    private readonly ChoiceModel<T> _choice;
    private readonly string _label;
    private readonly int _startIndex;
    private readonly int? _endIndex;
    private readonly bool _isFocusable;

    internal ChoiceFormRow(ChoiceModel<T> choice, string label, int startIndex = 0, int? endIndex = null, bool isFocusable = true)
    {
        _choice = choice;
        FormLabel parsed = FormLabelParser.Parse(label);
        _label = parsed.Text;
        Mnemonic = parsed.Mnemonic;
        _startIndex = startIndex;
        _endIndex = endIndex;
        _isFocusable = isFocusable;
    }

    internal ChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer = null,
        bool isFocusable = true)
        : this(new ChoiceModel<T>(ChoiceSelection<T>.FromValue(values, selectedValue, comparer), format), label, isFocusable: isFocusable)
    {
    }

    internal ChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        IEqualityComparer<T>? comparer = null,
        bool isFocusable = true)
        : this(new ChoiceModel<T>(ChoiceSelection<T>.FromValueOrFallback(values, selectedValue, fallbackValue, comparer), format), label, isFocusable: isFocusable)
    {
    }

    internal override bool IsFocusable => Enabled && _isFocusable;
    public bool Enabled { get; set; } = true;
    public string? DisabledReason { get; set; }
    internal override bool IsEnabled => Enabled;
    char? IFormMnemonic.Mnemonic => Mnemonic;
    private char? Mnemonic { get; }
    internal ChoiceModel<T> Choice => _choice;
    public T Value { get => _choice.Value; set => _choice.Value = value; }
    internal override int DesiredWidth => ConsoleTextMetrics.GetCellWidth(_label) + 1 + Math.Max(0, _choice.Selection.Items.Sum(item => ConsoleTextMetrics.GetCellWidth($"( ) {_choice.Format(item)}") + 1) - 1);

    internal override void Render(FormRowRenderContext context)
    {
        var layout = CalculateLayout(context.Bounds);
        ChoiceRenderer.Render(context.Canvas, layout, _choice.Selection, _choice.Format,
            !Enabled ? DisabledFormControlPresentation.WithReason(_label, DisabledReason) : _label,
            new(DisabledFormControlPresentation.Style(Enabled, DialogStyles.Fill), DisabledFormControlPresentation.Style(Enabled, DialogStyles.FocusedInput), context.Focused && Enabled));
    }

    bool IFormCursorProvider.TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        var layout = CalculateLayout(context.Bounds);
        if (Enabled && context.Focused && ChoiceRenderer.TryGetSelectedMarkerBounds(layout, _choice.Selection, out Rect bounds))
        {
            cursor = new FormCursorPlacement(bounds.X + 1, bounds.Y);
            return true;
        }

        cursor = default;
        return false;
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
        var layout = CalculateLayout(context.Bounds);
        return ToFormResult(ChoiceInput.HandleMouse(_choice.Selection, mouse, layout));
    }

    private ChoiceLayout CalculateLayout(Rect bounds) =>
        ChoiceLayoutCalculator.Segmented(_choice.Selection, _choice.Format, bounds, _label, _startIndex, _endIndex);

    private static FormInputResult ToFormResult(ChoiceInputResultKind result) => result switch
    {
        ChoiceInputResultKind.Handled => FormInputResult.Handled,
        ChoiceInputResultKind.ValueChanged => FormInputResult.ValueChanged,
        _ => FormInputResult.NotHandled,
    };
}

public sealed class MultiLineChoiceFormRow<T> : FormRow, IFormFocusTarget, IFormCursorProvider, IFormMnemonic
{
    internal override FormRowRole Role { get; init; } = FormRowRole.Option;

    private readonly ChoiceModel<T> _choice;
    private readonly string _label;
    private readonly IReadOnlyList<int> _segmentEndIndices;

    internal MultiLineChoiceFormRow(ChoiceModel<T> choice, string label, IReadOnlyList<int> segmentEndIndices)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(segmentEndIndices);
        if (segmentEndIndices.Count == 0)
            throw new ArgumentException("At least one segment is required.", nameof(segmentEndIndices));

        int previousEnd = 0;
        foreach (int endIndex in segmentEndIndices)
        {
            if (endIndex < previousEnd || endIndex > choice.Count)
                throw new ArgumentOutOfRangeException(nameof(segmentEndIndices), "Segment ends must be ordered choice indexes.");
            previousEnd = endIndex;
        }
        if (previousEnd != choice.Count)
            throw new ArgumentException("The final segment must include every choice.", nameof(segmentEndIndices));

        _choice = choice;
        FormLabel parsed = FormLabelParser.Parse(label);
        _label = parsed.Text;
        Mnemonic = parsed.Mnemonic;
        _segmentEndIndices = segmentEndIndices.ToArray();
    }

    internal MultiLineChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        int itemsPerRow,
        IEqualityComparer<T>? comparer = null)
        : this(new ChoiceModel<T>(ChoiceSelection<T>.FromValue(values, selectedValue, comparer), format), label, SegmentEndIndices(values, itemsPerRow))
    {
    }

    internal MultiLineChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        int itemsPerRow,
        IEqualityComparer<T>? comparer = null)
        : this(new ChoiceModel<T>(ChoiceSelection<T>.FromValueOrFallback(values, selectedValue, fallbackValue, comparer), format), label, SegmentEndIndices(values, itemsPerRow))
    {
    }

    internal override int Height => _segmentEndIndices.Count;
    internal ChoiceModel<T> Choice => _choice;
    public T Value { get => _choice.Value; set => _choice.Value = value; }
    public bool Enabled { get; set; } = true;
    public string? DisabledReason { get; set; }
    internal override bool IsEnabled => Enabled;
    char? IFormMnemonic.Mnemonic => Mnemonic;
    private char? Mnemonic { get; }
    internal override bool IsFocusable => Enabled;
    internal override int DesiredWidth => _segmentEndIndices.Select((end, index) =>
    {
        int start = index == 0 ? 0 : _segmentEndIndices[index - 1];
        return (index == 0 ? ConsoleTextMetrics.GetCellWidth(_label) + 1 : 0) +
            Math.Max(0, _choice.Selection.Items.Skip(start).Take(end - start).Sum(item => ConsoleTextMetrics.GetCellWidth($"( ) {_choice.Format(item)}") + 1) - 1);
    }).Max();

    internal override void Render(FormRowRenderContext context)
    {
        ChoiceRenderer.Render(context.Canvas, CalculateLayout(context.Bounds), _choice.Selection, _choice.Format,
            !Enabled ? DisabledFormControlPresentation.WithReason(_label, DisabledReason) : _label,
            new(DisabledFormControlPresentation.Style(Enabled, DialogStyles.Fill), DisabledFormControlPresentation.Style(Enabled, DialogStyles.FocusedInput), context.Focused && Enabled));
    }

    bool IFormCursorProvider.TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        var layout = CalculateLayout(context.Bounds);
        if (Enabled && context.Focused && ChoiceRenderer.TryGetSelectedMarkerBounds(layout, _choice.Selection, out Rect bounds))
        {
            cursor = new FormCursorPlacement(bounds.X + 1, bounds.Y);
            return true;
        }

        cursor = default;
        return false;
    }

    internal override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        return Enabled ? ToFormResult(ChoiceInput.HandleKey(_choice.Selection, key)) : FormInputResult.NotHandled;
    }

    internal override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        var layout = CalculateLayout(context.Bounds);
        return ToFormResult(ChoiceInput.HandleMouse(_choice.Selection, mouse, layout));
    }

    private ChoiceLayout CalculateLayout(Rect bounds) =>
        ChoiceLayoutCalculator.MultilineSegmented(_choice.Selection, _choice.Format, bounds, _label, _segmentEndIndices);

    private static FormInputResult ToFormResult(ChoiceInputResultKind result) => result switch
    {
        ChoiceInputResultKind.Handled => FormInputResult.Handled,
        ChoiceInputResultKind.ValueChanged => FormInputResult.ValueChanged,
        _ => FormInputResult.NotHandled,
    };

    private static IReadOnlyList<int> SegmentEndIndices(IReadOnlyList<T> values, int itemsPerRow)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (itemsPerRow <= 0)
            throw new ArgumentOutOfRangeException(nameof(itemsPerRow), "Items per row must be positive.");

        var ends = new List<int>();
        for (int end = itemsPerRow; end < values.Count; end += itemsPerRow)
            ends.Add(end);
        ends.Add(values.Count);
        return ends;
    }
}

