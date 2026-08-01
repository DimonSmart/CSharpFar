using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class DropdownSelectFormRow<T> : FormRow, IFormCursorProvider, IFormCompositeRow
{
    private readonly string _label;
    private readonly DropdownSelect<T> _dropdown;
    private bool _enabled = true;

    public DropdownSelectFormRow(string label, DropdownSelect<T> dropdown)
    {
        _label = label;
        _dropdown = dropdown;
    }

    public DropdownSelectFormRow(
        string label,
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        T selectedValue)
        : this(label, new DropdownSelect<T>(items, itemText))
    {
        _dropdown.SelectedIndex = FindSelectedIndex(items, selectedValue);
    }

    public override FormRowRole Role { get; init; } = FormRowRole.Option;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
                _dropdown.Close(commit: false);
        }
    }
    public string? DisabledReason { get; set; }
    public override bool IsEnabled => Enabled;
    public bool IsCompositeOpen => Enabled && _dropdown.IsOpen;
    public T Value => _dropdown.SelectedItem;
    public int SelectedIndex => _dropdown.SelectedIndex;
    public int MaxVisibleRows
    {
        get => _dropdown.MaxVisibleRows;
        set => _dropdown.MaxVisibleRows = value;
    }
    public int ConfirmedSelectedIndex => _dropdown.IsOpen
        ? _dropdown.SelectionBeforeOpen
        : _dropdown.SelectedIndex;
    public Rect GetFieldBounds(Rect rowBounds) => CalculateLayout(rowBounds).FieldBounds;

    public override void Render(FormRowRenderContext context)
    {
        var layout = CalculateLayout(context.Bounds);
        context.Canvas.Write(
            context.Bounds.X,
            context.Bounds.Y,
            ScrollableFormDialog.Fit(!Enabled ? DisabledFormControlPresentation.WithReason(_label, DisabledReason) : _label, layout.LabelWidth),
            DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.Fill));
        _dropdown.RenderField(
            context.Canvas,
            layout.FieldBounds,
            Enabled && context.Focused ? FarDialogStyles.FocusedInput : DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.Input));
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        Rect field = CalculateLayout(context.Bounds).FieldBounds;
        cursor = new FormCursorPlacement(field.X, field.Y);
        return Enabled && context.Focused && field.Width > 0;
    }

    public FormCompositeFrame BuildCompositeFrame(FormCompositeFrameContext context)
    {
        if (!Enabled)
            return new FormCompositeFrame(false, null, []);
        DropdownSelectFrame frame = _dropdown.CalculateFrame(context.Viewport.Size, CalculateLayout(context.RowBounds).FieldBounds);
        if (!frame.IsOpen || frame.PopupBounds is not Rect popup)
            return new FormCompositeFrame(false, frame, []);

        var children = new List<FormCompositeTarget> { new("popup", popup, Kind: FormTargetKind.DropdownPopup) };
        if (frame.ScrollbarBounds is Rect scrollbar)
            children.Add(new FormCompositeTarget("scrollbar", scrollbar, Kind: FormTargetKind.DropdownScrollbar, CapturesMouse: true));
        return new FormCompositeFrame(true, frame, children);
    }

    public void CommitCompositeFrame(FormCompositeFrame frame)
    {
        if (frame.State is DropdownSelectFrame dropdownFrame)
            _dropdown.ApplyCommittedFrame(dropdownFrame);
    }

    public void RenderCompositeOverlay(FormRowRenderContext context, FormCompositeFrame frame)
    {
        if (frame.State is DropdownSelectFrame dropdownFrame)
            _dropdown.RenderPopup(context.Canvas, dropdownFrame);
    }

    public bool IsCompositeAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame) =>
        frame.State is DropdownSelectFrame dropdownFrame && dropdownFrame.FieldBounds.Contains(mouse.X, mouse.Y);

    public void CloseComposite() => _dropdown.Close(commit: false);

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context) =>
        FormInputResult.NotHandled;

    public FormInputResult HandleCompositeKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        if (frame.State is not DropdownSelectFrame dropdownFrame)
            return FormInputResult.NotHandled;
        if (_dropdown.TryHandleKey(key, dropdownFrame, out _, out bool valueChanged))
        {
            if (valueChanged)
                return FormInputResult.ValueChanged;
            return dropdownFrame.IsOpen == _dropdown.IsOpen ? FormInputResult.Handled : FormInputResult.OverlayChanged;
        }

        return FormInputResult.NotHandled;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) =>
        FormInputResult.NotHandled;

    public FormInputResult HandleCompositeMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, string? childTargetId)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        if (frame.State is not DropdownSelectFrame dropdownFrame)
            return FormInputResult.NotHandled;
        bool valueChanged = false;
        bool handled = childTargetId switch
        {
            "scrollbar" => _dropdown.TryHandleScrollbarMouse(mouse, dropdownFrame),
            "popup" => _dropdown.TryHandlePopupContentMouse(mouse, dropdownFrame, out _, out valueChanged),
            null => _dropdown.TryHandleFieldMouse(mouse, dropdownFrame),
            _ => false,
        };
        if (handled)
            return valueChanged
                ? FormInputResult.ValueChanged
                : dropdownFrame.IsOpen == _dropdown.IsOpen ? FormInputResult.Handled : FormInputResult.OverlayChanged;
        return FormInputResult.NotHandled;
    }

    private DropdownSelectFormRowLayout CalculateLayout(Rect bounds)
    {
        int labelWidth = Math.Min(bounds.Width, _label.Length == 0 ? 0 : ConsoleTextMetrics.GetCellWidth(_label) + 1);
        int fieldX = bounds.X + labelWidth;
        return new DropdownSelectFormRowLayout(
            labelWidth,
            new Rect(fieldX, bounds.Y, Math.Max(0, bounds.Right - fieldX), 1));
    }

    private static int FindSelectedIndex(IReadOnlyList<T> items, T selectedValue)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(items[index], selectedValue))
                return index;
        }

        return 0;
    }

    private readonly record struct DropdownSelectFormRowLayout(int LabelWidth, Rect FieldBounds);
}

