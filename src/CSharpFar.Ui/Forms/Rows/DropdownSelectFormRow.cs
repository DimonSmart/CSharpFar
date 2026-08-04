using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class DropdownSelectFormRow<T> : FormRow, IFormCursorProvider, IFormCompositeOwner, IFormLabeledRow
{
    private readonly string _label;
    private readonly DropdownSelect<T> _dropdown;
    private bool _enabled = true;
    private readonly IFormCompositeController _compositeController;

    internal DropdownSelectFormRow(string label, DropdownSelect<T> dropdown)
    {
        _label = label;
        _dropdown = dropdown;
        _compositeController = new DropdownCompositeController<T>(_dropdown, () => Enabled);
    }

    internal DropdownSelectFormRow(
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
    int IFormLabeledRow.DesiredLabelWidth => ConsoleTextMetrics.GetCellWidth(_label);
    bool IFormLabeledRow.UseSharedLabelColumn => true;
    IFormCompositeController IFormCompositeOwner.CompositeController => _compositeController;
    public T Value
    {
        get => _dropdown.SelectedItem;
        set => _dropdown.SetSelectedValue(value);
    }
    internal int SelectedIndex => _dropdown.SelectedIndex;
    public int MaxVisibleRows
    {
        get => _dropdown.MaxVisibleRows;
        set => _dropdown.MaxVisibleRows = value;
    }

    public override void Render(FormRowRenderContext context)
    {
        if (context.Layout.LabelBounds is Rect labelBounds)
            context.Canvas.Write(
                labelBounds.X,
                labelBounds.Y,
                ScrollableFormDialog.Fit(!Enabled ? DisabledFormControlPresentation.WithReason(_label, DisabledReason) : _label, labelBounds.Width),
                DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.Fill));
        _dropdown.RenderField(
            context.Canvas,
            context.Layout.ControlBounds,
            Enabled && context.Focused ? FarDialogStyles.FocusedInput : DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.Input));
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        Rect field = context.Layout.ControlBounds;
        cursor = new FormCursorPlacement(field.X, field.Y);
        return Enabled && context.Focused && field.Width > 0;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        if (!Enabled || _dropdown.IsOpen || key.Key is not (ConsoleKey.Enter or ConsoleKey.Spacebar or ConsoleKey.DownArrow or ConsoleKey.F4))
            return FormInputResult.NotHandled;
        _dropdown.Open();
        return FormInputResult.OverlayChanged;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (!Enabled || _dropdown.IsOpen || mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down } || !context.Layout.ControlBounds.Contains(mouse.X, mouse.Y))
            return FormInputResult.NotHandled;
        _dropdown.Toggle();
        return FormInputResult.OverlayChanged;
    }


    private static int FindSelectedIndex(IReadOnlyList<T> items, T selectedValue)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(items[index], selectedValue))
                return index;
        }

        throw new ArgumentException("The selected value must be present in the dropdown items.", nameof(selectedValue));
    }

}

