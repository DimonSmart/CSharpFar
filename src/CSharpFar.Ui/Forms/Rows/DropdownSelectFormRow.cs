using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class DropdownSelectFormRow<T> : FormRow, IFormCursorProvider, IFormDropdownRow, IFormTransientOverlayRow
{
    private readonly string _label;
    private readonly DropdownSelect<T> _dropdown;

    public DropdownSelectFormRow(string label, DropdownSelect<T> dropdown)
    {
        _label = label;
        _dropdown = dropdown;
    }

    public override FormRowRole Role { get; init; } = FormRowRole.Option;
    public bool IsDropdownOpen => _dropdown.IsOpen;
    public bool IsOverlayOpen => _dropdown.IsOpen;
    public T Value => _dropdown.SelectedItem;
    public int SelectedIndex => _dropdown.SelectedIndex;
    public int ConfirmedSelectedIndex => _dropdown.IsOpen
        ? _dropdown.SelectionBeforeOpen
        : _dropdown.SelectedIndex;

    public override void Render(FormRowRenderContext context)
    {
        var layout = CalculateLayout(context.Bounds);
        context.Canvas.Write(
            context.Bounds.X,
            context.Bounds.Y,
            ScrollableFormDialog.Fit(_label.PadRight(layout.LabelWidth), layout.LabelWidth),
            FarDialogStyles.Fill);
        _dropdown.RenderField(
            context.Canvas,
            layout.FieldBounds,
            context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input);
    }

    public void RenderDropdownOverlay(FormRowRenderContext context, DropdownSelectFrame frame) =>
        _dropdown.RenderPopup(context.Canvas, frame);

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        Rect field = GetFieldBounds(context.Bounds);
        cursor = new FormCursorPlacement(field.X, field.Y);
        return context.Focused && field.Width > 0;
    }

    public Rect GetFieldBounds(Rect rowBounds) => CalculateLayout(rowBounds).FieldBounds;

    public DropdownSelectFrame BuildDropdownFrame(Rect rowBounds, ConsoleViewport viewport) =>
        _dropdown.CalculateFrame(viewport.Size, GetFieldBounds(rowBounds));

    public void CommitDropdownFrame(DropdownSelectFrame frame) =>
        _dropdown.ApplyCommittedFrame(frame);

    public void CloseDropdown() => _dropdown.Close();

    public void CancelOverlay() => _dropdown.Close(commit: false);

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context) =>
        FormInputResult.NotHandled;

    public FormInputResult HandleDropdownKey(ConsoleKeyInfo key, FormRowInputContext context, DropdownSelectFrame frame)
    {
        if (_dropdown.TryHandleKey(key, frame, out _, out bool valueChanged))
        {
            if (valueChanged)
                return FormInputResult.ValueChanged;
            return frame.IsOpen == _dropdown.IsOpen ? FormInputResult.Handled : FormInputResult.OverlayChanged;
        }

        return FormInputResult.NotHandled;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) =>
        FormInputResult.NotHandled;

    public FormInputResult HandleDropdownMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, DropdownSelectFrame frame)
    {
        if (_dropdown.TryHandlePopupMouse(mouse, frame, out _, out bool valueChanged))
            return valueChanged
                ? FormInputResult.ValueChanged
                : frame.IsOpen == _dropdown.IsOpen ? FormInputResult.Handled : FormInputResult.OverlayChanged;
        if (_dropdown.TryHandleFieldMouse(mouse, frame))
            return frame.IsOpen == _dropdown.IsOpen ? FormInputResult.Handled : FormInputResult.OverlayChanged;
        return FormInputResult.NotHandled;
    }

    private DropdownSelectFormRowLayout CalculateLayout(Rect bounds)
    {
        int labelWidth = Math.Min(bounds.Width, _label.Length == 0 ? 0 : _label.Length + 1);
        int fieldX = bounds.X + labelWidth;
        return new DropdownSelectFormRowLayout(
            labelWidth,
            new Rect(fieldX, bounds.Y, Math.Max(0, bounds.Right - fieldX), 1));
    }

    private readonly record struct DropdownSelectFormRowLayout(int LabelWidth, Rect FieldBounds);
}

