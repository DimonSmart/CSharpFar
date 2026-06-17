using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public enum FormInputResultKind
{
    NotHandled,
    Handled,
    ValueChanged,
    MoveFocusNext,
    MoveFocusPrevious,
    Submit,
    Cancel,
}

public readonly record struct FormInputResult(FormInputResultKind Kind, string? Command = null)
{
    public static FormInputResult NotHandled => new(FormInputResultKind.NotHandled);
    public static FormInputResult Handled => new(FormInputResultKind.Handled);
    public static FormInputResult ValueChanged => new(FormInputResultKind.ValueChanged);
    public static FormInputResult MoveFocusNext => new(FormInputResultKind.MoveFocusNext);
    public static FormInputResult MoveFocusPrevious => new(FormInputResultKind.MoveFocusPrevious);
    public static FormInputResult Submit(string? command = null) => new(FormInputResultKind.Submit, command);
    public static FormInputResult Cancel(string? command = null) => new(FormInputResultKind.Cancel, command);

    public bool IsHandled => Kind != FormInputResultKind.NotHandled;
}

public interface IFormRow
{
    bool IsFocusable { get; }
    int Height { get; }
    void Render(FormRowRenderContext context);
    FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context);
    FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context);
}

public interface IFormOverlayRow
{
    void RenderOverlay(FormRowRenderContext context);
}

public readonly record struct FormCursorPlacement(int X, int Y);

public interface IFormCursorProvider
{
    bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor);
}

public sealed class FormRenderContext
{
    public FormRenderContext(ScreenRenderer screen, Rect bodyBounds, CellStyle? scrollbarStyle = null)
    {
        Screen = screen;
        BodyBounds = bodyBounds;
        ScrollbarStyle = scrollbarStyle ?? FarDialogStyles.Border;
    }

    public ScreenRenderer Screen { get; }
    public Rect BodyBounds { get; }
    public CellStyle ScrollbarStyle { get; }
}

public sealed class FormRowRenderContext
{
    public FormRowRenderContext(ScreenRenderer screen, Rect bounds, bool focused)
    {
        Screen = screen;
        Bounds = bounds;
        Focused = focused;
    }

    public ScreenRenderer Screen { get; }
    public Rect Bounds { get; }
    public bool Focused { get; }
}

public sealed class FormRowInputContext
{
    public FormRowInputContext(int rowIndex, bool focused, int availableDropdownContentRows = 0)
    {
        RowIndex = rowIndex;
        Focused = focused;
        AvailableDropdownContentRows = availableDropdownContentRows;
    }

    public int RowIndex { get; }
    public bool Focused { get; }
    public int AvailableDropdownContentRows { get; }
}

public sealed class FormRowMouseContext
{
    public FormRowMouseContext(Rect bounds, int rowIndex, bool focused, int screenHeight)
    {
        Bounds = bounds;
        RowIndex = rowIndex;
        Focused = focused;
        ScreenHeight = screenHeight;
    }

    public Rect Bounds { get; }
    public int RowIndex { get; }
    public bool Focused { get; }
    public int ScreenHeight { get; }
}

public abstract class FormRow : IFormRow
{
    public virtual bool IsFocusable => true;
    public virtual int Height => 1;
    public abstract void Render(FormRowRenderContext context);
    public virtual FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context) => FormInputResult.NotHandled;
    public virtual FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) => FormInputResult.NotHandled;
}

public sealed class LabelRow : FormRow
{
    private readonly string _text;
    private readonly CellStyle _style;

    public LabelRow(string text, CellStyle style)
    {
        _text = text;
        _style = style;
    }

    public override bool IsFocusable => false;

    public override void Render(FormRowRenderContext context) =>
        context.Screen.Write(context.Bounds.X, context.Bounds.Y, ScrollableFormDialog.Fit(_text, context.Bounds.Width), _style);
}

public sealed class SeparatorRow : FormRow
{
    private readonly CellStyle _style;
    private readonly bool _drawLine;

    public SeparatorRow(CellStyle style, bool drawLine = true)
    {
        _style = style;
        _drawLine = drawLine;
    }

    public override bool IsFocusable => false;

    public override void Render(FormRowRenderContext context)
    {
        if (context.Bounds.Width <= 0)
            return;

        string text = _drawLine ? new string('─', context.Bounds.Width) : string.Empty.PadRight(context.Bounds.Width);
        context.Screen.Write(context.Bounds.X, context.Bounds.Y, text, _style);
    }
}

public sealed class ButtonRow : FormRow
{
    private readonly DialogButtonBar _buttonBar;
    private readonly CellStyle _normalStyle;
    private readonly CellStyle _focusedStyle;

    public ButtonRow(IReadOnlyList<DialogButton> buttons, CellStyle normalStyle, CellStyle focusedStyle)
        : this(new DialogButtonBar(buttons), normalStyle, focusedStyle)
    {
    }

    public ButtonRow(DialogButtonBar buttonBar, CellStyle normalStyle, CellStyle focusedStyle)
    {
        _buttonBar = buttonBar;
        _normalStyle = normalStyle;
        _focusedStyle = focusedStyle;
    }

    public int FocusedButtonIndex { get; private set; }

    public override void Render(FormRowRenderContext context) =>
        _buttonBar.Render(
            context.Screen,
            context.Bounds.X,
            context.Bounds.Y,
            context.Bounds.Width,
            FocusedButtonIndex,
            _normalStyle,
            context.Focused ? _focusedStyle : _normalStyle);

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        int focusedButton = FocusedButtonIndex;
        if (!_buttonBar.TryHandleInput(new KeyConsoleInputEvent(key), ref focusedButton, out string? buttonId))
            return FormInputResult.NotHandled;

        FocusedButtonIndex = focusedButton;
        return ButtonResult(buttonId);
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        int focusedButton = FocusedButtonIndex;
        if (!_buttonBar.TryHandleInput(mouse, ref focusedButton, out string? buttonId))
            return FormInputResult.NotHandled;

        FocusedButtonIndex = focusedButton;
        return ButtonResult(buttonId);
    }

    private static FormInputResult ButtonResult(string? buttonId) =>
        buttonId switch
        {
            null => FormInputResult.Handled,
            "cancel" => FormInputResult.Cancel(buttonId),
            _ => FormInputResult.Submit(buttonId),
        };
}

public sealed class TextInputRow : FormRow, IFormOverlayRow, IFormCursorProvider
{
    private readonly CommandLineState _buffer;
    private readonly SingleLineTextHistoryState? _history;
    private readonly TextInputRowState _state;
    private readonly int? _width;

    public TextInputRow(CommandLineState buffer, SingleLineTextHistoryState? history = null, TextInputRowState? state = null, int? width = null)
    {
        _buffer = buffer;
        _history = history;
        _state = state ?? new TextInputRowState();
        _width = width;
    }

    public CommandLineState Buffer => _buffer;
    public SingleLineTextHistoryState? History => _history;
    public TextInputRowState State => _state;

    public override void Render(FormRowRenderContext context)
    {
        int width = Math.Min(context.Bounds.Width, _width ?? context.Bounds.Width);
        SingleLineTextInput.Render(
            context.Screen,
            context.Bounds.X,
            context.Bounds.Y,
            width,
            _buffer,
            context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input,
            FarDialogStyles.Input,
            _history,
            renderDropdown: false);

    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        int width = Math.Min(context.Bounds.Width, _width ?? context.Bounds.Width);
        int textWidth = _history is null ? width : Math.Max(1, width - 1);
        int cursorX = Math.Min(
            context.Bounds.X + textWidth - 1,
            SingleLineTextInput.GetCursorX(context.Bounds.X, textWidth, _buffer));
        cursor = new FormCursorPlacement(cursorX, context.Bounds.Y);
        return context.Focused && width > 0;
    }

    public void RenderOverlay(FormRowRenderContext context)
    {
        if (_history is null || !context.Focused)
            return;

        int width = Math.Min(context.Bounds.Width, _width ?? context.Bounds.Width);
        SingleLineTextInput.RenderHistoryDropdown(context.Screen, context.Bounds.X, context.Bounds.Y, width, _history);
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        string? error = null;
        string before = _buffer.Text;
        TextInputKeyResult result = SingleLineTextInput.HandleKey(
            _buffer,
            key,
            ref error,
            _history,
            context.AvailableDropdownContentRows);

        return result switch
        {
            TextInputKeyResult.TextChanged when _buffer.Text != before => FormInputResult.ValueChanged,
            TextInputKeyResult.TextChanged => FormInputResult.Handled,
            TextInputKeyResult.Handled => FormInputResult.Handled,
            _ => FormInputResult.NotHandled,
        };
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        int width = Math.Min(context.Bounds.Width, _width ?? context.Bounds.Width);
        if (_history is not null &&
            SingleLineTextInput.TryHandleHistoryDropdownMouse(
                _history,
                _buffer,
                mouse,
                context.Bounds.X,
                context.Bounds.Y,
                width,
                context.ScreenHeight,
                ref _state.HistoryScrollbarDrag))
        {
            return FormInputResult.ValueChanged;
        }

        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click) ||
            mouse.Y != context.Bounds.Y ||
            mouse.X < context.Bounds.X ||
            mouse.X >= context.Bounds.X + width)
        {
            return FormInputResult.NotHandled;
        }

        if (_history is not null &&
            SingleLineTextInput.IsHistoryArrowHit(context.Bounds.X, width, context.Bounds.Y, mouse.X, mouse.Y))
        {
            return SingleLineTextInput.TryOpenHistoryDropdown(_history, context.Bounds.Y, context.ScreenHeight)
                ? FormInputResult.Handled
                : FormInputResult.NotHandled;
        }

        int textWidth = _history is null ? width : Math.Max(1, width - 1);
        int target = Math.Clamp(mouse.X - context.Bounds.X, 0, Math.Min(_buffer.Text.Length, textWidth));
        _buffer.MoveCursor(target - _buffer.CursorPosition);
        return FormInputResult.Handled;
    }
}

public sealed class TextInputRowState
{
    public ScrollBarDragState? HistoryScrollbarDrag;
}

public sealed class TextInputWithButtonsRow : FormRow, IFormOverlayRow, IFormCursorProvider
{
    private readonly string _label;
    private readonly CommandLineState _buffer;
    private readonly TextInputRowState _state;
    private readonly DialogButtonBar _buttonBar;
    private readonly int _inputWidth;
    private readonly int _buttonAreaWidth;
    private readonly string _commandPrefix;
    private Rect _inputBounds;
    private Rect _buttonBounds;

    public TextInputWithButtonsRow(
        string label,
        CommandLineState buffer,
        IReadOnlyList<DialogButton> buttons,
        string commandPrefix,
        int inputWidth,
        int buttonAreaWidth,
        TextInputRowState? state = null)
    {
        _label = label;
        _buffer = buffer;
        _buttonBar = new DialogButtonBar(buttons);
        _commandPrefix = commandPrefix;
        _inputWidth = inputWidth;
        _buttonAreaWidth = buttonAreaWidth;
        _state = state ?? new TextInputRowState();
    }

    public CommandLineState Buffer => _buffer;
    public TextInputRowState State => _state;

    public override void Render(FormRowRenderContext context)
    {
        int labelWidth = Math.Min(_label.Length + 1, Math.Max(0, context.Bounds.Width));
        context.Screen.Write(
            context.Bounds.X,
            context.Bounds.Y,
            ScrollableFormDialog.Fit(_label.PadRight(labelWidth), labelWidth),
            FarDialogStyles.Fill);

        int inputX = context.Bounds.X + labelWidth;
        int remainingAfterLabel = Math.Max(0, context.Bounds.Width - labelWidth);
        int inputWidth = Math.Min(_inputWidth, remainingAfterLabel);
        _inputBounds = new Rect(inputX, context.Bounds.Y, inputWidth, 1);
        SingleLineTextInput.Render(
            context.Screen,
            _inputBounds.X,
            _inputBounds.Y,
            _inputBounds.Width,
            _buffer,
            context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input,
            FarDialogStyles.Input,
            history: null,
            renderDropdown: false);

        int buttonX = _inputBounds.Right + 1;
        int buttonWidth = Math.Min(_buttonAreaWidth, Math.Max(0, context.Bounds.Right - buttonX));
        _buttonBounds = new Rect(buttonX, context.Bounds.Y, buttonWidth, 1);
        if (buttonWidth > 0)
        {
            _buttonBar.Render(
                context.Screen,
                _buttonBounds.X,
                _buttonBounds.Y,
                _buttonBounds.Width,
                focusedIndex: 0,
                FarDialogStyles.Fill,
                context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill);
        }
    }

    public void RenderOverlay(FormRowRenderContext context)
    {
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        int cursorX = Math.Min(
            _inputBounds.Right - 1,
            SingleLineTextInput.GetCursorX(_inputBounds.X, _inputBounds.Width, _buffer));
        cursor = new FormCursorPlacement(cursorX, _inputBounds.Y);
        return context.Focused && _inputBounds.Width > 0;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        string? error = null;
        string before = _buffer.Text;
        TextInputKeyResult result = SingleLineTextInput.HandleKey(
            _buffer,
            key,
            ref error,
            history: null,
            availableDropdownContentRows: 0);

        return result switch
        {
            TextInputKeyResult.TextChanged when _buffer.Text != before => FormInputResult.ValueChanged,
            TextInputKeyResult.TextChanged => FormInputResult.Handled,
            TextInputKeyResult.Handled => FormInputResult.Handled,
            _ => FormInputResult.NotHandled,
        };
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        int focusedButton = 0;
        if (_buttonBar.TryHandleInput(mouse, ref focusedButton, out string? buttonId))
            return buttonId is null
                ? FormInputResult.Handled
                : FormInputResult.Submit(_commandPrefix + buttonId);

        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click) ||
            mouse.Y != _inputBounds.Y ||
            mouse.X < _inputBounds.X ||
            mouse.X >= _inputBounds.Right)
        {
            return FormInputResult.NotHandled;
        }

        int target = Math.Clamp(mouse.X - _inputBounds.X, 0, Math.Min(_buffer.Text.Length, _inputBounds.Width));
        _buffer.MoveCursor(target - _buffer.CursorPosition);
        return FormInputResult.Handled;
    }
}

public sealed class CheckBoxRow : FormRow, IFormCursorProvider
{
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

    public override void Render(FormRowRenderContext context) =>
        _checkBox.Render(context.Screen, context.Bounds.X, context.Bounds.Y, context.Bounds.Width, context.Focused);

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        cursor = new FormCursorPlacement(context.Bounds.X + 1, context.Bounds.Y);
        return context.Focused && context.Bounds.Width >= 3;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        bool before = _checkBox.Value;
        if (!_checkBox.TryHandleKey(key))
            return FormInputResult.NotHandled;

        return _checkBox.Value != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        bool before = _checkBox.Value;
        if (!_checkBox.TryHandleMouse(mouse))
            return FormInputResult.NotHandled;

        return _checkBox.Value != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }
}

public sealed class TriStateCheckBoxRow : FormRow, IFormCursorProvider
{
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
        if (!_checkBox.TryHandleMouse(mouse))
            return FormInputResult.NotHandled;

        return _checkBox.Value != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }
}

public sealed class ChoiceFormRow<T> : FormRow, IFormCursorProvider
{
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

    public override void Render(FormRowRenderContext context) =>
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

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        if (context.Focused && _choice.TryGetSelectedMarkerBounds(out Rect bounds))
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
        if (!_choice.TryHandleMouse(mouse))
            return FormInputResult.NotHandled;

        return _choice.SelectedIndex != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }
}

public sealed class MultiLineChoiceFormRow<T> : FormRow, IFormCursorProvider
{
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
        if (context.Focused && _choice.TryGetSelectedMarkerBounds(out Rect bounds))
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
        if (!_choice.TryHandleMouse(mouse))
            return FormInputResult.NotHandled;

        return _choice.SelectedIndex != before ? FormInputResult.ValueChanged : FormInputResult.Handled;
    }
}

public sealed class ScrollableFormDialog
{
    private IReadOnlyList<IFormRow> _rows = [];
    private Rect _lastBodyBounds;
    private int _lastViewportRows = 1;
    private int _lastScreenHeight = 1;

    public ScrollableFormDialog()
    {
    }

    public ScrollableFormDialog(IReadOnlyList<IFormRow> rows)
    {
        SetRows(rows);
    }

    public int FocusIndex { get; private set; }
    public int FocusableCount => FocusableRowCount;
    public int ScrollTop { get; private set; }
    public ScrollBarDragState? ScrollbarDrag { get; private set; }

    private int BodyRowCount => TotalRowHeight;
    private int FocusableRowCount => _rows.Count(static row => row.IsFocusable);

    private int TotalRowHeight => _rows.Sum(static row => Math.Max(1, row.Height));

    public void SetRows(IReadOnlyList<IFormRow> rows)
    {
        _rows = rows;
        FocusIndex = ClampFocusIndex(FocusIndex);
        if (FocusableRowCount > 0 && !IsFocusableAtFocusIndex(FocusIndex))
            FocusIndex = NextFocusableIndex(FocusIndex, 1);
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, BodyRowCount, _lastViewportRows);
    }

    public void Render(FormRenderContext context)
    {
        context.Screen.SetCursorVisible(false);
        _lastBodyBounds = context.BodyBounds;
        _lastViewportRows = Math.Max(1, context.BodyBounds.Height);
        _lastScreenHeight = context.Screen.GetSize().Height;
        EnsureFocusVisible(_lastViewportRows);

        context.Screen.FillRegion(context.BodyBounds, FarDialogStyles.Fill);
        int virtualTop = 0;
        int focusIndex = 0;
        FormRowRenderContext? focusedRenderContext = null;
        IFormCursorProvider? cursorProvider = null;
        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            IFormRow row = _rows[rowIndex];
            int rowHeight = Math.Max(1, row.Height);
            bool visible = virtualTop + rowHeight > ScrollTop && virtualTop < ScrollTop + _lastViewportRows;
            if (visible)
            {
                int y = context.BodyBounds.Y + virtualTop - ScrollTop;
                var rowBounds = new Rect(context.BodyBounds.X, y, context.BodyBounds.Width, rowHeight);
                bool focused = row.IsFocusable && focusIndex == FocusIndex;
                var rowContext = new FormRowRenderContext(context.Screen, rowBounds, focused);
                row.Render(rowContext);
                if (focused && row is IFormCursorProvider provider)
                {
                    focusedRenderContext = rowContext;
                    cursorProvider = provider;
                }
            }

            if (row.IsFocusable)
                focusIndex++;
            virtualTop += rowHeight;
        }

        if (BodyRowCount > _lastViewportRows)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                context.Screen,
                new Rect(context.BodyBounds.Right - 1, context.BodyBounds.Y, 1, _lastViewportRows),
                new ScrollState
                {
                    TotalItems = BodyRowCount,
                    ViewportItems = _lastViewportRows,
                    FirstVisibleIndex = ScrollTop,
                },
                new ScrollBarOptions
                {
                    Enabled = true,
                    DrawWhenNotScrollable = false,
                },
                context.ScrollbarStyle);
        }

        RenderFocusedOverlay(context.Screen);

        if (cursorProvider is not null &&
            focusedRenderContext is not null &&
            cursorProvider.TryGetCursor(focusedRenderContext, out FormCursorPlacement cursor) &&
            cursor.X >= context.BodyBounds.X && cursor.X < context.BodyBounds.Right &&
            cursor.Y >= context.BodyBounds.Y && cursor.Y < context.BodyBounds.Bottom)
        {
            context.Screen.SetCursorPosition(cursor.X, cursor.Y);
            context.Screen.SetCursorVisible(true);
        }
        else
        {
            context.Screen.SetCursorVisible(false);
        }
    }

    public FormInputResult HandleKey(ConsoleKeyInfo key)
    {
        if (FocusableRowCount == 0)
            return FormInputResult.NotHandled;

        int availableDropdownRows = TryGetFocusedRowBounds(out Rect focusedBounds)
            ? SingleLineTextInput.AvailableDropdownContentRows(focusedBounds.Y, _lastScreenHeight)
            : 0;
        FormInputResult rowResult = FocusedRow()?.HandleKey(
            key,
            new FormRowInputContext(FocusIndex, focused: true, availableDropdownRows)) ?? FormInputResult.NotHandled;
        if (rowResult.IsHandled)
            return ApplyResult(rowResult);

        FormInputResult result = key.Key switch
        {
            ConsoleKey.UpArrow => MoveFocusResult(-1),
            ConsoleKey.DownArrow => MoveFocusResult(1),
            ConsoleKey.PageUp => MoveFocusPage(-1),
            ConsoleKey.PageDown => MoveFocusPage(1),
            ConsoleKey.Home => SetFocusResult(FirstFocusableIndex()),
            ConsoleKey.End => SetFocusResult(LastFocusableIndex()),
            ConsoleKey.Tab when (key.Modifiers & ConsoleModifiers.Shift) != 0 => MoveFocusResult(-1),
            ConsoleKey.Tab => MoveFocusResult(1),
            ConsoleKey.Escape => FormInputResult.Cancel(),
            _ => FormInputResult.NotHandled,
        };
        return result;
    }

    public FormInputResult HandleMouse(MouseConsoleInputEvent mouse)
    {
        if (TryHandleWheel(mouse, _lastViewportRows))
            return FormInputResult.Handled;

        if (FocusedRow() is not null && TryGetFocusedRowBounds(out Rect focusedBounds))
        {
            FormInputResult focusedResult = FocusedRow()!.HandleMouse(
                mouse,
                new FormRowMouseContext(focusedBounds, FocusIndex, focused: true, _lastScreenHeight));
            if (focusedResult.IsHandled)
                return ApplyResult(focusedResult);
        }

        if (BodyRowCount > _lastViewportRows &&
            TryHandleScrollbarMouse(
                mouse,
                new Rect(_lastBodyBounds.Right - 1, _lastBodyBounds.Y, 1, _lastViewportRows),
                _lastViewportRows))
        {
            return FormInputResult.Handled;
        }

        if (!TryHitTestRow(_lastBodyBounds, _lastViewportRows, mouse.X, mouse.Y, out int rowIndex, out Rect rowBounds, out int focusIndex))
            return FormInputResult.NotHandled;

        IFormRow row = _rows[rowIndex];
        if (!row.IsFocusable)
        {
            FormInputResult nonFocusableResult = row.HandleMouse(
                mouse,
                new FormRowMouseContext(rowBounds, rowIndex, focused: false, _lastScreenHeight));
            return nonFocusableResult.IsHandled ? ApplyResult(nonFocusableResult) : FormInputResult.NotHandled;
        }

        FocusIndex = focusIndex;
        EnsureFocusVisible(_lastViewportRows);
        FormInputResult result = row.HandleMouse(mouse, new FormRowMouseContext(rowBounds, FocusIndex, focused: true, _lastScreenHeight));
        return result.IsHandled ? ApplyResult(result) : FormInputResult.Handled;
    }

    private void RenderFocusedOverlay(ScreenRenderer screen)
    {
        if (FocusedRow() is not IFormOverlayRow overlayRow)
            return;

        if (!TryGetFocusedRowBounds(out Rect bounds))
            return;

        overlayRow.RenderOverlay(new FormRowRenderContext(screen, bounds, focused: true));
    }

    private void MoveFocus(int delta, int viewportRows)
    {
        if (delta == 0)
            return;

        FocusIndex = NextFocusableIndex(FocusIndex, delta);
        EnsureFocusVisible(viewportRows);
    }

    private void EnsureFocusVisible(int viewportRows)
    {
        int clampedViewportRows = Math.Max(1, viewportRows);
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, BodyRowCount, clampedViewportRows);

        int focusVirtualRow = FocusIndexToVirtualRow(FocusIndex);
        if (focusVirtualRow >= 0)
        {
            ScrollTop = ScrollStateCalculator.EnsureIndexVisible(focusVirtualRow, ScrollTop, clampedViewportRows);
            int focusHeight = Math.Max(1, FocusedRow()?.Height ?? 1);
            if (focusHeight <= clampedViewportRows && focusVirtualRow + focusHeight > ScrollTop + clampedViewportRows)
                ScrollTop = focusVirtualRow + focusHeight - clampedViewportRows;
        }

        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, BodyRowCount, clampedViewportRows);
    }

    private bool TryHandleWheel(MouseConsoleInputEvent mouse, int viewportRows, int wheelRows = 3)
    {
        if (mouse.Kind != MouseEventKind.Wheel)
            return false;

        int delta = mouse.Button switch
        {
            MouseButton.WheelUp => -Math.Abs(wheelRows),
            MouseButton.WheelDown => Math.Abs(wheelRows),
            _ => 0,
        };
        if (delta == 0)
            return false;

        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop + delta, BodyRowCount, Math.Max(1, viewportRows));
        return true;
    }

    private bool TryHandleScrollbarMouse(MouseConsoleInputEvent mouse, Rect scrollbarBounds, int viewportRows)
    {
        int firstVisibleIndex = ScrollTop;
        var dragState = ScrollbarDrag;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                mouse,
                scrollbarBounds,
                BodyRowCount,
                Math.Max(1, viewportRows),
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(firstVisibleIndex, BodyRowCount, Math.Max(1, viewportRows));
        ScrollbarDrag = dragState;
        return true;
    }

    private FormInputResult ApplyResult(FormInputResult result)
    {
        switch (result.Kind)
        {
            case FormInputResultKind.MoveFocusNext:
                return MoveFocusResult(1);
            case FormInputResultKind.MoveFocusPrevious:
                return MoveFocusResult(-1);
            default:
                return result;
        }
    }

    private FormInputResult MoveFocusResult(int delta)
    {
        MoveFocus(delta, _lastViewportRows);
        return FormInputResult.Handled;
    }

    private FormInputResult MoveFocusPage(int delta)
    {
        int targetVirtual = Math.Clamp(FocusIndexToVirtualRow(FocusIndex) + delta * _lastViewportRows, 0, Math.Max(0, BodyRowCount - 1));
        FocusIndex = NearestFocusableIndexAtOrAfterVirtualRow(targetVirtual, delta);
        EnsureFocusVisible(_lastViewportRows);
        return FormInputResult.Handled;
    }

    private FormInputResult SetFocusResult(int focusIndex)
    {
        FocusIndex = ClampFocusIndex(focusIndex);
        EnsureFocusVisible(_lastViewportRows);
        return FormInputResult.Handled;
    }

    private IFormRow? FocusedRow()
    {
        int focusIndex = 0;
        foreach (IFormRow row in _rows)
        {
            if (!row.IsFocusable)
                continue;

            if (focusIndex == FocusIndex)
                return row;

            focusIndex++;
        }

        return null;
    }

    private bool TryGetFocusedRowBounds(out Rect bounds)
    {
        bounds = default;
        int currentFocusIndex = 0;
        int currentVirtual = 0;
        foreach (IFormRow row in _rows)
        {
            int height = Math.Max(1, row.Height);
            if (row.IsFocusable)
            {
                if (currentFocusIndex == FocusIndex)
                {
                    bounds = new Rect(_lastBodyBounds.X, _lastBodyBounds.Y + currentVirtual - ScrollTop, _lastBodyBounds.Width, height);
                    return true;
                }

                currentFocusIndex++;
            }

            currentVirtual += height;
        }

        return false;
    }

    private bool TryHitTestRow(Rect contentBounds, int viewportRows, int x, int y, out int rowIndex, out Rect rowBounds, out int focusIndex)
    {
        rowIndex = -1;
        rowBounds = default;
        focusIndex = -1;
        if (!TryHitTestBodyRow(contentBounds, ScrollTop, BodyRowCount, viewportRows, x, y, out int virtualRow))
            return false;

        int currentVirtualRow = 0;
        int currentFocusIndex = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            IFormRow row = _rows[i];
            int height = Math.Max(1, row.Height);
            if (virtualRow >= currentVirtualRow && virtualRow < currentVirtualRow + height)
            {
                rowIndex = i;
                rowBounds = new Rect(contentBounds.X, contentBounds.Y + currentVirtualRow - ScrollTop, contentBounds.Width, height);
                focusIndex = row.IsFocusable ? currentFocusIndex : -1;
                return true;
            }

            if (row.IsFocusable)
                currentFocusIndex++;
            currentVirtualRow += height;
        }

        return false;
    }

    private int NextFocusableIndex(int start, int delta)
    {
        int count = FocusableRowCount;
        if (count <= 0)
            return 0;

        int step = delta < 0 ? -1 : 1;
        int row = ClampFocusIndex(start);
        for (int i = 0; i < count; i++)
        {
            row = (row + step + count) % count;
            if (IsFocusableAtFocusIndex(row))
                return row;
        }

        return ClampFocusIndex(start);
    }

    private int FirstFocusableIndex() => 0;
    private int LastFocusableIndex() => Math.Max(0, FocusableRowCount - 1);

    private int ClampFocusIndex(int focusRow)
    {
        int count = FocusableRowCount;
        return count <= 0 ? 0 : Math.Clamp(focusRow, 0, count - 1);
    }

    private bool IsFocusableAtFocusIndex(int focusRow)
    {
        return focusRow >= 0 && focusRow < FocusableRowCount;
    }

    private int FocusIndexToVirtualRow(int focusIndex)
    {
        int currentFocusRow = 0;
        int virtualRow = 0;
        foreach (IFormRow row in _rows)
        {
            if (row.IsFocusable)
            {
                if (currentFocusRow == focusIndex)
                    return virtualRow;

                currentFocusRow++;
            }

            virtualRow += Math.Max(1, row.Height);
        }

        return -1;
    }

    private int NearestFocusableIndexAtOrAfterVirtualRow(int virtualRow, int direction)
    {
        int currentFocusIndex = 0;
        int bestBefore = 0;
        for (int i = 0, currentVirtual = 0; i < _rows.Count; i++)
        {
            IFormRow row = _rows[i];
            if (row.IsFocusable)
            {
                if (currentVirtual >= virtualRow)
                    return currentFocusIndex;

                bestBefore = currentFocusIndex;
                currentFocusIndex++;
            }

            currentVirtual += Math.Max(1, row.Height);
        }

        return direction > 0 ? LastFocusableIndex() : bestBefore;
    }

    private static bool TryHitTestBodyRow(
        Rect contentBounds,
        int bodyScrollTop,
        int bodyRowCount,
        int viewportRows,
        int x,
        int y,
        out int virtualRow)
    {
        virtualRow = -1;
        if (x < contentBounds.X || x >= contentBounds.Right ||
            y < contentBounds.Y || y >= contentBounds.Y + Math.Max(1, viewportRows))
        {
            return false;
        }

        virtualRow = bodyScrollTop + y - contentBounds.Y;
        return virtualRow >= 0 && virtualRow < bodyRowCount;
    }

    internal static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
