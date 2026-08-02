using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Shared interaction mechanics for a single-line form text field.</summary>
internal sealed class FormTextInputField
{
    private readonly CommandLineState _buffer;
    private readonly SingleLineTextHistoryState? _history;
    private readonly bool _maskInput;
    private bool _enabled = true;

    public FormTextInputField(CommandLineState buffer, SingleLineTextHistoryState? history, bool maskInput = false)
    {
        _buffer = buffer;
        _history = history;
        _maskInput = maskInput;
    }

    public CommandLineState Buffer => _buffer;
    public SingleLineTextHistoryState? History => _history;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
                _history?.Close();
        }
    }
    public string? DisabledReason { get; set; }

    public void Render(FormRowRenderContext context, Rect bounds) =>
        SingleLineTextInput.Render(context.Canvas, bounds.X, bounds.Y, bounds.Width, _buffer,
            Enabled && context.Focused ? FarDialogStyles.FocusedInput : DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.Input),
            DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.Input), Enabled ? _history : null, maskInput: _maskInput, renderDropdown: false);

    public void RenderOverlay(FormRowRenderContext context, Rect bounds)
    {
        if (Enabled && _history is not null && context.Focused)
            SingleLineTextInput.RenderHistoryDropdown(context.Canvas, bounds.X, bounds.Y, bounds.Width, _history, context.CanvasHeight);
    }

    public bool TryGetCursor(FormRowRenderContext context, Rect bounds, out FormCursorPlacement cursor)
    {
        int textWidth = _history is null ? bounds.Width : Math.Max(1, bounds.Width - 1);
        cursor = new FormCursorPlacement(Math.Min(bounds.Right - 1, SingleLineTextInput.GetCursorX(bounds.X, textWidth, _buffer)), bounds.Y);
        return Enabled && context.Focused && bounds.Width > 0;
    }

    public FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        string? error = null;
        string before = _buffer.Text;
        TextInputKeyResult result = SingleLineTextInput.HandleKey(_buffer, key, ref error, _history, context.AvailableOverlayContentRows);
        return result switch
        {
            TextInputKeyResult.TextChanged when _buffer.Text != before => FormInputResult.ValueChanged,
            TextInputKeyResult.TextChanged or TextInputKeyResult.Handled => FormInputResult.Handled,
            TextInputKeyResult.AcceptCurrentText => FormInputResult.OverlayChanged,
            _ => FormInputResult.NotHandled,
        };
    }

    public FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, Rect bounds)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        if (mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down } || !bounds.Contains(mouse.X, mouse.Y))
            return FormInputResult.NotHandled;

        if (_history is not null && SingleLineTextInput.IsHistoryArrowHit(bounds.X, bounds.Width, bounds.Y, mouse.X, mouse.Y))
        {
            if (!_history.History.HasItems)
                return FormInputResult.Handled;
            if (_history.IsDropdownOpen)
            {
                _history.Close();
                return FormInputResult.Handled;
            }
            return SingleLineTextInput.TryOpenHistoryDropdown(_history, bounds.Y, context.CanvasHeight) ? FormInputResult.Handled : FormInputResult.NotHandled;
        }

        int textWidth = _history is null ? bounds.Width : Math.Max(1, bounds.Width - 1);
        int cursorCell = ConsoleTextMetrics.CellOffsetFromUtf16Index(_buffer.Text, _buffer.CursorPosition);
        int visibleStart = ConsoleTextMetrics.Utf16IndexFromCellOffset(_buffer.Text, Math.Max(0, cursorCell - Math.Max(0, textWidth - 1)));
        int visibleStartCell = ConsoleTextMetrics.CellOffsetFromUtf16Index(_buffer.Text, visibleStart);
        int target = ConsoleTextMetrics.Utf16IndexFromCellOffset(_buffer.Text, visibleStartCell + mouse.X - bounds.X);
        _buffer.MoveCursor(target - _buffer.CursorPosition);
        return FormInputResult.Handled;
    }

    public bool IsHistoryArrow(MouseConsoleInputEvent mouse, Rect bounds) =>
        Enabled && _history is not null && SingleLineTextInput.IsHistoryArrowHit(bounds.X, bounds.Width, bounds.Y, mouse.X, mouse.Y);

    public FormCompositeFrame BuildCompositeFrame(Rect bounds, ConsoleViewport viewport, UiTargetId rowTarget)
    {
        SingleLineTextHistoryFrame? frame = !Enabled || _history is null
            ? null
            : SingleLineTextInput.CalculateHistoryDropdownFrame(bounds.X, bounds.Y, bounds.Width, viewport.Height, _history);
        if (frame is not { } value)
            return FormCompositeFrame.Closed();

        var children = new List<FormCompositeTarget>
        {
            new(FormTargetIds.ForHistoryDropdown(rowTarget), value.PopupBounds, Kind: FormTargetKind.HistoryDropdown),
        };
        if (value.ScrollbarBounds is Rect scrollbar)
            children.Add(new FormCompositeTarget(FormTargetIds.ForHistoryScrollbar(rowTarget), scrollbar, Kind: FormTargetKind.HistoryScrollbar, CapturesMouse: true));
        return FormCompositeFrame.Open(new TextHistoryCompositeSnapshot(value), children);
    }

    public void RenderCompositeOverlay(FormRowRenderContext context, FormCompositeFrame frame)
    {
        if (_history is not null && frame.Snapshot is TextHistoryCompositeSnapshot { Frame: var historyFrame })
            SingleLineTextInput.RenderHistoryDropdown(context.Canvas, _history, historyFrame);
    }

    public FormInputResult HandleCompositeMouse(
        MouseConsoleInputEvent mouse,
        FormRowMouseContext context,
        Rect bounds,
        FormCompositeFrame frame,
        UiTargetId? childTargetId)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        string before = _buffer.Text;
        if (_history is not null && frame.Snapshot is TextHistoryCompositeSnapshot { Frame: var historyFrame })
        {
            var currentFrame = historyFrame with
            {
                FirstVisibleIndex = _history.FirstVisibleIndex,
                VerticalScrollbarFrame = _history.Scrollbar.CalculateFrame(historyFrame.ScrollbarBounds, new ScrollState
                {
                    TotalItems = _history.Matches.Count,
                    ViewportItems = historyFrame.VisibleRows,
                    FirstVisibleIndex = _history.FirstVisibleIndex,
                }),
            };
            bool handled = childTargetId switch
            {
                { } target when frame.ChildTargets.FirstOrDefault(child => child.Id == target)?.Kind == FormTargetKind.HistoryScrollbar => SingleLineTextInput.TryHandleHistoryScrollbarMouse(_history, mouse, currentFrame),
                { } target when frame.ChildTargets.FirstOrDefault(child => child.Id == target)?.Kind == FormTargetKind.HistoryDropdown => SingleLineTextInput.TryHandleHistoryPopupContentMouse(_history, _buffer, mouse, currentFrame),
                _ => false,
            };
            if (handled)
                return _buffer.Text != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
        }

        return HandleMouse(mouse, context, bounds);
    }
}
