using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class ChoiceFormRow<T> : FormRow, IFormCursorProvider
{
    public override FormRowRole Role { get; init; } = FormRowRole.Option;

    private readonly ChoiceRow<T> _choice;
    private readonly string _label;
    private readonly int _startIndex;
    private readonly int? _endIndex;
    private readonly bool _isFocusable;

    public ChoiceFormRow(ChoiceRow<T> choice, string label, int startIndex = 0, int? endIndex = null, bool isFocusable = true)
    {
        _choice = choice;
        _label = label;
        _startIndex = startIndex;
        _endIndex = endIndex;
        _isFocusable = isFocusable;
    }

    public ChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer = null,
        bool isFocusable = true)
        : this(ChoiceRow<T>.FromValue(values, format, selectedValue, comparer), label, isFocusable: isFocusable)
    {
    }

    public ChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        IEqualityComparer<T>? comparer = null,
        bool isFocusable = true)
        : this(ChoiceRow<T>.FromValue(values, format, selectedValue, fallbackValue, comparer), label, isFocusable: isFocusable)
    {
    }

    public override bool IsFocusable => Enabled && _isFocusable;
    public bool Enabled { get; set; } = true;
    public string? DisabledReason { get; set; }
    public override bool IsEnabled => Enabled;
    public ChoiceRow<T> Choice => _choice;
    public T Value => _choice.Value;

    public override void Render(FormRowRenderContext context)
    {
        var layout = CalculateLayout(context.Bounds);
        ChoiceRenderer.Render(context.Canvas, layout, _choice.Selection, _choice.Format,
            !Enabled ? DisabledFormControlPresentation.WithReason(_label, DisabledReason) : _label,
            new(DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.Fill), DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.FocusedInput), context.Focused && Enabled));
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
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

public sealed class MultiLineChoiceFormRow<T> : FormRow, IFormCursorProvider
{
    public override FormRowRole Role { get; init; } = FormRowRole.Option;

    private readonly ChoiceRow<T> _choice;
    private readonly string _label;
    private readonly IReadOnlyList<int> _segmentEndIndices;

    public MultiLineChoiceFormRow(ChoiceRow<T> choice, string label, IReadOnlyList<int> segmentEndIndices)
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
        _label = label;
        _segmentEndIndices = segmentEndIndices.ToArray();
    }

    public MultiLineChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        int itemsPerRow,
        IEqualityComparer<T>? comparer = null)
        : this(ChoiceRow<T>.FromValue(values, format, selectedValue, comparer), label, SegmentEndIndices(values, itemsPerRow))
    {
    }

    public MultiLineChoiceFormRow(
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        int itemsPerRow,
        IEqualityComparer<T>? comparer = null)
        : this(ChoiceRow<T>.FromValue(values, format, selectedValue, fallbackValue, comparer), label, SegmentEndIndices(values, itemsPerRow))
    {
    }

    public override int Height => _segmentEndIndices.Count;
    public ChoiceRow<T> Choice => _choice;
    public T Value => _choice.Value;

    public override void Render(FormRowRenderContext context)
    {
        ChoiceRenderer.Render(context.Canvas, CalculateLayout(context.Bounds), _choice.Selection, _choice.Format, _label,
            new(FarDialogStyles.Fill, FarDialogStyles.FocusedInput, context.Focused));
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        var layout = CalculateLayout(context.Bounds);
        if (context.Focused && ChoiceRenderer.TryGetSelectedMarkerBounds(layout, _choice.Selection, out Rect bounds))
        {
            cursor = new FormCursorPlacement(bounds.X + 1, bounds.Y);
            return true;
        }

        cursor = default;
        return false;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        return ToFormResult(ChoiceInput.HandleKey(_choice.Selection, key));
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
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

