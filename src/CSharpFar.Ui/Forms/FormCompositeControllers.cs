using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Ui;

internal sealed record TextHistoryCompositeSnapshot(SingleLineTextHistoryFrame Frame) : IFormCompositeSnapshot;
internal sealed record DropdownCompositeSnapshot(DropdownSelectFrame Frame) : IFormCompositeSnapshot;

internal sealed class TextInputCompositeController : IFormCompositeController, IFormCompositeCommitController
{
    private readonly FormTextInputField _field;
    private readonly Func<FormRowLayout, Rect> _inputBounds;

    public TextInputCompositeController(FormTextInputField field, Func<FormRowLayout, Rect> inputBounds)
    {
        _field = field;
        _inputBounds = inputBounds;
    }

    public bool IsOpen => _field.Enabled && _field.History?.IsDropdownOpen == true;

    public FormCompositeFrame CalculateFrame(FormCompositeFrameContext context) =>
        _field.BuildCompositeFrame(_inputBounds(context.Layout), context.Viewport, context.RowTarget);

    public void ApplyCommittedFrame(FormCompositeFrame frame)
    {
        if (_field.History is not null && frame.State is TextHistoryCompositeSnapshot { Frame: var historyFrame })
        {
            _field.History.ApplyCommittedSnapshot(historyFrame.Snapshot, historyFrame.MatchSetVersion);
            _field.History.Scrollbar.ApplyCommittedFrame(historyFrame.VerticalScrollbarFrame);
        }
    }
    public void RenderOverlay(FormRowRenderContext context, FormCompositeFrame frame) => _field.RenderCompositeOverlay(context, frame);
    public FormInputResult RouteMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, UiTargetId? childTarget)
    {
        Rect bounds = _inputBounds(context.Layout);
        if (childTarget is null && _field.IsHistoryArrow(mouse, bounds))
        {
            if (mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down })
                return FormInputResult.Handled;

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
    public void Close(bool commit) => _field.History?.Close();
}

internal sealed class DropdownCompositeController<T> : IFormCompositeController, IFormCompositeKeyboardController, IFormCompositeCommitController, IFormCompositeInputFrameController
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
        DropdownSelectFrame frame = _dropdown.CalculateFrame(context.Viewport.Size, context.Layout.ControlBounds);
        if (!_isEnabled())
            return FormCompositeFrame.Closed(new DropdownCompositeSnapshot(frame));
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
        _dropdown.ApplyCommittedFrame(GetDropdownFrame(frame));
    }

    public void RestoreInputFrame(FormCompositeFrame frame) => _dropdown.RestoreCommittedFrame(GetDropdownFrame(frame));

    public void RenderOverlay(FormRowRenderContext context, FormCompositeFrame frame)
    {
        if (frame.State is DropdownCompositeSnapshot { Frame: var dropdownFrame })
            _dropdown.RenderPopup(context.Canvas, dropdownFrame);
    }

    public FormInputResult RouteOverlayKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame)
    {
        if (!_isEnabled())
            return FormInputResult.NotHandled;
        DropdownInputResult result = _dropdown.TryHandleKey(key, GetDropdownFrame(frame));
        return ToFormResult(result);
    }

    public FormInputResult RouteMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, UiTargetId? childTarget)
    {
        if (!_isEnabled())
            return FormInputResult.NotHandled;
        DropdownSelectFrame dropdownFrame = GetDropdownFrame(frame);
        DropdownInputResult result = DropdownInputResult.NotHandled;
        foreach (FormCompositeTarget target in frame.Overlay?.ChildTargets ?? [])
        {
            if (target.Id != childTarget)
                continue;
            result = target.Kind switch
            {
                FormTargetKind.DropdownScrollbar => _dropdown.TryHandleScrollbarMouse(mouse, dropdownFrame),
                FormTargetKind.DropdownPopup => _dropdown.TryHandlePopupContentMouse(mouse, dropdownFrame),
                _ => DropdownInputResult.NotHandled,
            };
            break;
        }
        if (childTarget is null)
            result = _dropdown.TryHandleFieldMouse(mouse, dropdownFrame);
        return ToFormResult(result);
    }

    public bool IsAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame) =>
        frame.State is DropdownCompositeSnapshot { Frame: var dropdownFrame } && dropdownFrame.FieldBounds.Contains(mouse.X, mouse.Y);
    public void Close(bool commit) => _dropdown.Close(commit);

    private static DropdownSelectFrame GetDropdownFrame(FormCompositeFrame frame) => frame.State switch
    {
        DropdownCompositeSnapshot { Frame: var value } => value,
        _ => throw new InvalidOperationException("Dropdown composite frame has an incompatible component state."),
    };

    private static FormInputResult ToFormResult(DropdownInputResult result) => result.Kind switch
    {
        DropdownInputResultKind.NotHandled => FormInputResult.NotHandled,
        DropdownInputResultKind.Committed => FormInputResult.ValueChanged,
        DropdownInputResultKind.Opened or DropdownInputResultKind.Canceled => new(FormInputResultKind.OverlayChanged, MouseCapture: result.MouseCapture),
        _ => new(FormInputResultKind.Handled, MouseCapture: result.MouseCapture),
    };
}
