using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

internal sealed record TextHistoryCompositeSnapshot(SingleLineTextHistoryFrame Frame) : IFormCompositeSnapshot;
internal sealed record DropdownCompositeSnapshot(DropdownSelectFrame Frame) : IFormCompositeSnapshot, IFormCompositeCommittedState;

internal sealed class TextInputCompositeController : IFormCompositeController
{
    private readonly FormTextInputField _field;
    private readonly Func<FormRowLayout, Rect> _inputBounds;
    private readonly Func<ConsoleKeyInfo, FormRowInputContext, FormInputResult> _routeKey;

    public TextInputCompositeController(FormTextInputField field, Func<FormRowLayout, Rect> inputBounds, Func<ConsoleKeyInfo, FormRowInputContext, FormInputResult> routeKey)
    {
        _field = field;
        _inputBounds = inputBounds;
        _routeKey = routeKey;
    }

    public bool IsOpen => _field.Enabled && _field.History?.IsDropdownOpen == true;

    public FormCompositeFrame CalculateFrame(FormCompositeFrameContext context) =>
        _field.BuildCompositeFrame(_inputBounds(context.Layout), context.Viewport, context.RowTarget);

    public void ApplyCommittedFrame(FormCompositeFrame frame) { }
    public void RenderOverlay(FormRowRenderContext context, FormCompositeFrame frame) => _field.RenderCompositeOverlay(context, frame);
    public FormInputResult RouteKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame) => _routeKey(key, context);
    public FormInputResult RouteMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, UiTargetId? childTarget)
    {
        Rect bounds = _inputBounds(context.Layout);
        if (childTarget is null && _field.IsHistoryArrow(mouse, bounds))
        {
            SingleLineTextHistoryState? history = _field.History;
            if (history is null || !history.History.HasItems)
                return FormInputResult.Handled;
            if (history.IsDropdownOpen)
                history.Close();
            else
                history.OpenAll(int.MaxValue);
            return FormInputResult.OverlayChanged;
        }

        return _field.HandleCompositeMouse(mouse, context, bounds, frame, childTarget);
    }
    public bool IsAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame) =>
        _field.IsHistoryArrow(mouse, _inputBounds(context.Layout));
    public void Close() => _field.History?.Close();
}

internal sealed class DropdownCompositeController<T> : IFormCompositeController
{
    private readonly DropdownSelect<T> _dropdown;
    private readonly Func<bool> _isEnabled;

    public DropdownCompositeController(DropdownSelect<T> dropdown, Func<bool> isEnabled)
    {
        _dropdown = dropdown;
        _isEnabled = isEnabled;
    }
    public bool IsOpen => _isEnabled() && _dropdown.IsOpen;

    public FormCompositeFrame CalculateFrame(FormCompositeFrameContext context)
    {
        if (!_isEnabled())
            return FormCompositeFrame.Closed();
        DropdownSelectFrame frame = _dropdown.CalculateFrame(context.Viewport.Size, context.Layout.ControlBounds);
        if (frame.Popup is not { } popupFrame)
            return FormCompositeFrame.Closed(new DropdownCompositeSnapshot(frame));

        var targets = new List<FormCompositeTarget>
        {
            new(FormTargetIds.ForDropdownPopup(context.RowTarget), popupFrame.Bounds, Kind: FormTargetKind.DropdownPopup),
        };
        if (popupFrame.List.ScrollbarBounds is Rect scrollbar)
            targets.Add(new(FormTargetIds.ForDropdownScrollbar(context.RowTarget), scrollbar, Kind: FormTargetKind.DropdownScrollbar, CapturesMouse: true));
        return FormCompositeFrame.Open(new DropdownCompositeSnapshot(frame), targets);
    }

    public void ApplyCommittedFrame(FormCompositeFrame frame)
    {
        if (GetDropdownFrame(frame) is { } dropdownFrame)
            _dropdown.ApplyCommittedFrame(dropdownFrame);
    }

    public void RenderOverlay(FormRowRenderContext context, FormCompositeFrame frame)
    {
        if (frame.Snapshot is DropdownCompositeSnapshot { Frame: var dropdownFrame })
            _dropdown.RenderPopup(context.Canvas, dropdownFrame);
    }

    public FormInputResult RouteKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame)
    {
        if (!_isEnabled())
            return FormInputResult.NotHandled;
        if (GetDropdownFrame(frame) is not { } dropdownFrame)
        {
            if (key.Key is not (ConsoleKey.Enter or ConsoleKey.Spacebar or ConsoleKey.DownArrow or ConsoleKey.F4))
                return FormInputResult.NotHandled;
            _dropdown.Open();
            return FormInputResult.OverlayChanged;
        }
        if (!_dropdown.TryHandleKey(key, dropdownFrame, out _, out bool valueChanged))
            return FormInputResult.NotHandled;
        return valueChanged ? FormInputResult.ValueChanged : dropdownFrame.IsOpen == _dropdown.IsOpen ? FormInputResult.Handled : FormInputResult.OverlayChanged;
    }

    public FormInputResult RouteMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, UiTargetId? childTarget)
    {
        if (!_isEnabled())
            return FormInputResult.NotHandled;
        if (GetDropdownFrame(frame) is not { } dropdownFrame)
        {
            Rect fieldBounds = context.Layout.ControlBounds;
            if (mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down } || !fieldBounds.Contains(mouse.X, mouse.Y))
                return FormInputResult.NotHandled;
            _dropdown.Toggle();
            return FormInputResult.OverlayChanged;
        }
        bool valueChanged = false;
        bool handled = false;
        foreach (FormCompositeTarget target in frame.ChildTargets)
        {
            if (target.Id != childTarget)
                continue;
            handled = target.Kind switch
            {
                FormTargetKind.DropdownScrollbar => _dropdown.TryHandleScrollbarMouse(mouse, dropdownFrame),
                FormTargetKind.DropdownPopup => _dropdown.TryHandlePopupContentMouse(mouse, dropdownFrame, out _, out valueChanged),
                _ => false,
            };
            break;
        }
        if (childTarget is null)
            handled = _dropdown.TryHandleFieldMouse(mouse, dropdownFrame);
        return !handled ? FormInputResult.NotHandled : valueChanged ? FormInputResult.ValueChanged : dropdownFrame.IsOpen == _dropdown.IsOpen ? FormInputResult.Handled : FormInputResult.OverlayChanged;
    }

    public bool IsAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame) =>
        frame.Snapshot is DropdownCompositeSnapshot { Frame: var dropdownFrame } && dropdownFrame.FieldBounds.Contains(mouse.X, mouse.Y);
    public void Close() => _dropdown.Close(commit: false);

    private static DropdownSelectFrame? GetDropdownFrame(FormCompositeFrame frame) => frame.Snapshot switch
    {
        DropdownCompositeSnapshot { Frame: var value } => value,
        _ when frame.CommittedState is DropdownCompositeSnapshot { Frame: var value } => value,
        _ => null,
    };
}
