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

    public override bool IsFocusable => _isFocusable;
    public ChoiceRow<T> Choice => _choice;
    public T Value => _choice.Value;

    public override void Render(FormRowRenderContext context)
    {
        var layout = CalculateLayout(context.Bounds);
        _choice.RenderSegmented(
            context.Screen,
            context.Bounds.X,
            context.Bounds.Y,
            context.Bounds.Width,
            _label,
            context.Focused,
            FarDialogStyles.Fill,
            FarDialogStyles.FocusedInput,
            _startIndex,
            _endIndex);
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        var layout = CalculateLayout(context.Bounds);
        if (context.Focused && _choice.TryGetSelectedMarkerBounds(layout, out Rect bounds))
        {
            cursor = new FormCursorPlacement(bounds.X + 1, bounds.Y);
            return true;
        }

        cursor = default;
        return false;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        int before = _choice.SelectedIndex;
        if (!_choice.TryHandleKey(key))
            return FormInputResult.NotHandled;

        return _choice.SelectedIndex != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        int before = _choice.SelectedIndex;
        var layout = CalculateLayout(context.Bounds);
        if (!_choice.TryHandleMouse(mouse, layout))
            return FormInputResult.NotHandled;

        return _choice.SelectedIndex != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }

    private ChoiceRowLayout CalculateLayout(Rect bounds) =>
        _choice.CalculateSegmentedLayout(bounds.X, bounds.Y, bounds.Width, _label, _startIndex, _endIndex);
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

    public override int Height => _segmentEndIndices.Count;
    public ChoiceRow<T> Choice => _choice;
    public T Value => _choice.Value;

    public override void Render(FormRowRenderContext context)
    {
        int startIndex = 0;
        for (int line = 0; line < _segmentEndIndices.Count; line++)
        {
            int endIndex = _segmentEndIndices[line];
            _choice.RenderSegmented(
                context.Screen,
                context.Bounds.X,
                context.Bounds.Y + line,
                context.Bounds.Width,
                line == 0 ? _label : string.Empty,
                context.Focused,
                FarDialogStyles.Fill,
                FarDialogStyles.FocusedInput,
                startIndex,
                endIndex);
            startIndex = endIndex;
        }
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        var layout = CalculateLayout(context.Bounds);
        if (context.Focused && _choice.TryGetSelectedMarkerBounds(layout, out Rect bounds))
        {
            cursor = new FormCursorPlacement(bounds.X + 1, bounds.Y);
            return true;
        }

        cursor = default;
        return false;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        int before = _choice.SelectedIndex;
        if (!_choice.TryHandleKey(key))
            return FormInputResult.NotHandled;

        return _choice.SelectedIndex != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        int before = _choice.SelectedIndex;
        var layout = CalculateLayout(context.Bounds);
        if (!_choice.TryHandleMouse(mouse, layout))
            return FormInputResult.NotHandled;

        return _choice.SelectedIndex != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }

    private ChoiceRowLayout CalculateLayout(Rect bounds)
    {
        var rowBounds = new List<Rect>();
        var choices = new List<ChoiceHitTarget>();
        int startIndex = 0;
        for (int line = 0; line < _segmentEndIndices.Count; line++)
        {
            int endIndex = _segmentEndIndices[line];
            var lineLayout = _choice.CalculateSegmentedLayout(
                bounds.X,
                bounds.Y + line,
                bounds.Width,
                line == 0 ? _label : string.Empty,
                startIndex,
                endIndex);
            rowBounds.AddRange(lineLayout.RowBounds);
            choices.AddRange(lineLayout.Choices);
            startIndex = endIndex;
        }

        return new ChoiceRowLayout(ChoiceRowLayoutKind.Segmented, rowBounds, choices);
    }
}

