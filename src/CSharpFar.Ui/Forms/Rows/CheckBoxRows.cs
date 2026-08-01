using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class CheckBoxRow : FormRow, IFormCursorProvider
{
    public override FormRowRole Role { get; init; } = FormRowRole.Option;

    private readonly CheckBoxLine _checkBox;

    public CheckBoxRow(CheckBoxLine checkBox)
    {
        _checkBox = checkBox;
    }

    public CheckBoxRow(string label, bool value = false)
        : this(new CheckBoxLine(label, value))
    {
    }

    public bool Value
    {
        get => _checkBox.Value;
        set => _checkBox.Value = value;
    }

    public bool Enabled { get; set; } = true;
    public override bool IsEnabled => Enabled;
    public bool ShowCursor { get; init; } = true;

    public override void Render(FormRowRenderContext context)
    {
        CellStyle fill = Enabled
            ? FarDialogStyles.Fill
            : FarDialogStyles.DisabledControl(FarDialogStyles.Fill);

        _checkBox.Render(
            context.Canvas,
            context.Bounds.X,
            context.Bounds.Y,
            context.Bounds.Width,
            context.Focused && Enabled,
            fill,
            FarDialogStyles.FocusedInput);
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        cursor = new FormCursorPlacement(context.Bounds.X + 1, context.Bounds.Y);
        return ShowCursor && Enabled && context.Focused && context.Bounds.Width >= 3;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        bool before = _checkBox.Value;
        if (!_checkBox.TryHandleKey(key))
            return FormInputResult.NotHandled;

        return _checkBox.Value != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        bool before = _checkBox.Value;
        if (!_checkBox.TryHandleMouse(mouse, context.Bounds))
            return FormInputResult.NotHandled;

        return _checkBox.Value != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }
}

public sealed class TriStateCheckBoxRow : FormRow, IFormCursorProvider
{
    public override FormRowRole Role { get; init; } = FormRowRole.Option;

    private readonly TriStateCheckBoxLine _checkBox;

    internal TriStateCheckBoxRow(TriStateCheckBoxLine checkBox)
    {
        _checkBox = checkBox;
    }

    public TriStateCheckBoxRow(string id, string label, CheckState value = CheckState.Unchecked)
        : this(new TriStateCheckBoxLine(label, value))
    {
        Id = id;
    }

    public CheckState Value
    {
        get => _checkBox.Value;
        set => _checkBox.Value = value;
    }

    public bool Enabled
    {
        get => _checkBox.Enabled;
        set => _checkBox.Enabled = value;
    }

    public string? DisabledReason { get; set; }

    public override bool IsEnabled => Enabled;

    public override void Render(FormRowRenderContext context)
    {
        CellStyle fill = Enabled
            ? FarDialogStyles.Fill
            : FarDialogStyles.DisabledControl(FarDialogStyles.Fill);
        string label = DisabledReason is { Length: > 0 }
            ? $"{_checkBox.Label} - {DisabledReason}"
            : _checkBox.Label;
        var display = new TriStateCheckBoxLine(label, _checkBox.Value);
        display.Render(
            context.Canvas,
            context.Bounds.X,
            context.Bounds.Y,
            context.Bounds.Width,
            context.Focused && Enabled,
            fill,
            FarDialogStyles.FocusedInput);
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        cursor = new FormCursorPlacement(context.Bounds.X + 1, context.Bounds.Y);
        return Enabled && context.Focused && context.Bounds.Width >= 3;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        CheckState before = _checkBox.Value;
        if (!_checkBox.TryHandleKey(key))
            return FormInputResult.NotHandled;

        return _checkBox.Value != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        CheckState before = _checkBox.Value;
        if (!_checkBox.TryHandleMouse(mouse, context.Bounds))
            return FormInputResult.NotHandled;

        return _checkBox.Value != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }
}

