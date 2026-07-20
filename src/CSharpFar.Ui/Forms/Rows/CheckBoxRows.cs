using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class CheckBoxRow : FormRow, IFormCursorProvider
{
    public override FormRowRole Role { get; init; } = FormRowRole.Option;

    private readonly CheckBoxLine _checkBox;

    public CheckBoxRow(CheckBoxLine checkBox)
    {
        _checkBox = checkBox;
    }

    public bool Value
    {
        get => _checkBox.Value;
        set => _checkBox.Value = value;
    }

    public bool Enabled { get; set; } = true;
    public override bool IsEnabled => Enabled;
    public bool ShowCursor { get; init; } = true;

    public override void Render(FormRowRenderContext context) =>
        _checkBox.Render(context.Screen, context.Bounds.X, context.Bounds.Y, context.Bounds.Width, context.Focused && Enabled);

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        cursor = new FormCursorPlacement(context.Bounds.X + 1, context.Bounds.Y);
        return ShowCursor && context.Focused && context.Bounds.Width >= 3;
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

    public TriStateCheckBoxRow(TriStateCheckBoxLine checkBox)
    {
        _checkBox = checkBox;
    }

    public AttributeEditState Value
    {
        get => _checkBox.Value;
        set => _checkBox.Value = value;
    }

    public bool Enabled
    {
        get => _checkBox.Enabled;
        set => _checkBox.Enabled = value;
    }

    public override bool IsEnabled => Enabled;

    public override void Render(FormRowRenderContext context) =>
        _checkBox.Render(context.Screen, context.Bounds.X, context.Bounds.Y, context.Bounds.Width, context.Focused);

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        cursor = new FormCursorPlacement(context.Bounds.X + 1, context.Bounds.Y);
        return context.Focused && context.Bounds.Width >= 3;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        AttributeEditState before = _checkBox.Value;
        if (!_checkBox.TryHandleKey(key))
            return FormInputResult.NotHandled;

        return _checkBox.Value != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        AttributeEditState before = _checkBox.Value;
        if (!_checkBox.TryHandleMouse(mouse, context.Bounds))
            return FormInputResult.NotHandled;

        return _checkBox.Value != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }
}

