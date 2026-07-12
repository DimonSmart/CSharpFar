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

public enum FormRowRole
{
    Normal,
    TextInput,
    Option,
    ButtonBar,
}

public interface IFormRow
{
    string? Id { get; }
    FormRowRole Role { get; }
    bool SubmitOnEnter { get; }
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
    private readonly UiRenderContext _renderContext;

    public FormRenderContext(
        UiRenderContext renderContext,
        Rect bodyBounds,
        CellStyle? scrollbarStyle = null,
        Rect? footerBounds = null)
    {
        ArgumentNullException.ThrowIfNull(renderContext);

        _renderContext = renderContext;
        BodyBounds = bodyBounds;
        ScrollbarStyle = scrollbarStyle ?? FarDialogStyles.Border;
        FooterBounds = footerBounds;
    }

    public ScreenRenderer Screen => _renderContext.Screen;
    public ConsoleViewport Viewport => _renderContext.Viewport;
    public Rect BodyBounds { get; }
    public CellStyle ScrollbarStyle { get; }
    public Rect? FooterBounds { get; }

    public void PublishOnStable(Action commit) => _renderContext.PublishOnStable(commit);
}

public sealed class FormRowRenderContext
{
    public FormRowRenderContext(ScreenRenderer screen, Rect bounds, bool focused, int? screenHeight = null)
    {
        Screen = screen;
        Bounds = bounds;
        Focused = focused;
        ScreenHeight = screenHeight ?? screen.FrameViewport.Height;
    }

    public ScreenRenderer Screen { get; }
    public Rect Bounds { get; }
    public bool Focused { get; }
    public int ScreenHeight { get; }
}

public sealed class FormRowInputContext
{
    public FormRowInputContext(
        int rowIndex,
        bool focused,
        int availableDropdownContentRows = 0,
        string? rowId = null,
        FormRowRole rowRole = FormRowRole.Normal)
    {
        RowIndex = rowIndex;
        Focused = focused;
        AvailableDropdownContentRows = availableDropdownContentRows;
        RowId = rowId;
        RowRole = rowRole;
    }

    public int RowIndex { get; }
    public bool Focused { get; }
    public int AvailableDropdownContentRows { get; }
    public string? RowId { get; }
    public FormRowRole RowRole { get; }
}

public sealed class FormRowMouseContext
{
    public FormRowMouseContext(
        Rect bounds,
        int rowIndex,
        bool focused,
        int screenHeight,
        string? rowId = null,
        FormRowRole rowRole = FormRowRole.Normal)
    {
        Bounds = bounds;
        RowIndex = rowIndex;
        Focused = focused;
        ScreenHeight = screenHeight;
        RowId = rowId;
        RowRole = rowRole;
    }

    public Rect Bounds { get; }
    public int RowIndex { get; }
    public bool Focused { get; }
    public int ScreenHeight { get; }
    public string? RowId { get; }
    public FormRowRole RowRole { get; }
}

public abstract class FormRow : IFormRow
{
    public virtual string? Id { get; init; }
    public virtual FormRowRole Role { get; init; } = FormRowRole.Normal;
    public virtual bool SubmitOnEnter { get; init; }
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
    public override FormRowRole Role { get; init; } = FormRowRole.ButtonBar;

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
        if (!_buttonBar.TryHandleKey(key, ref focusedButton, out string? buttonId))
            return FormInputResult.NotHandled;

        FocusedButtonIndex = focusedButton;
        return ButtonResult(buttonId);
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        int focusedButton = FocusedButtonIndex;
        var layout = _buttonBar.CalculateLayout(context.Bounds.X, context.Bounds.Y, context.Bounds.Width);
        if (!_buttonBar.TryHandleMouse(mouse, layout, ref focusedButton, out string? buttonId))
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
    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;
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
        SingleLineTextInput.RenderHistoryDropdown(context.Screen, context.Bounds.X, context.Bounds.Y, width, _history, context.ScreenHeight);
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
    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;

    private readonly string _label;
    private readonly CommandLineState _buffer;
    private readonly TextInputRowState _state;
    private readonly DialogButtonBar _buttonBar;
    private readonly int _inputWidth;
    private readonly int _buttonAreaWidth;
    private readonly string _commandPrefix;

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
        var layout = CalculateLayout(context.Bounds);
        int labelWidth = layout.InputBounds.X - context.Bounds.X;
        context.Screen.Write(
            context.Bounds.X,
            context.Bounds.Y,
            ScrollableFormDialog.Fit(_label.PadRight(labelWidth), labelWidth),
            FarDialogStyles.Fill);

        SingleLineTextInput.Render(
            context.Screen,
            layout.InputBounds.X,
            layout.InputBounds.Y,
            layout.InputBounds.Width,
            _buffer,
            context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input,
            FarDialogStyles.Input,
            history: null,
            renderDropdown: false);

        if (layout.ButtonAreaBounds.Width > 0)
        {
            _buttonBar.Render(
                context.Screen,
                layout.ButtonLayout,
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
        var layout = CalculateLayout(context.Bounds);
        int cursorX = Math.Min(
            layout.InputBounds.Right - 1,
            SingleLineTextInput.GetCursorX(layout.InputBounds.X, layout.InputBounds.Width, _buffer));
        cursor = new FormCursorPlacement(cursorX, layout.InputBounds.Y);
        return context.Focused && layout.InputBounds.Width > 0;
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
        var layout = CalculateLayout(context.Bounds);
        int focusedButton = 0;
        if (_buttonBar.TryHandleMouse(mouse, layout.ButtonLayout, ref focusedButton, out string? buttonId))
            return buttonId is null
                ? FormInputResult.Handled
                : FormInputResult.Submit(_commandPrefix + buttonId);

        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click) ||
            mouse.Y != layout.InputBounds.Y ||
            mouse.X < layout.InputBounds.X ||
            mouse.X >= layout.InputBounds.Right)
        {
            return FormInputResult.NotHandled;
        }

        int target = Math.Clamp(mouse.X - layout.InputBounds.X, 0, Math.Min(_buffer.Text.Length, layout.InputBounds.Width));
        _buffer.MoveCursor(target - _buffer.CursorPosition);
        return FormInputResult.Handled;
    }

    private TextInputWithButtonsLayout CalculateLayout(Rect rowBounds)
    {
        int labelWidth = Math.Min(_label.Length + 1, Math.Max(0, rowBounds.Width));
        int inputX = rowBounds.X + labelWidth;
        int remainingAfterLabel = Math.Max(0, rowBounds.Width - labelWidth);
        int inputWidth = Math.Min(_inputWidth, remainingAfterLabel);
        var inputBounds = new Rect(inputX, rowBounds.Y, inputWidth, 1);
        int buttonX = inputBounds.Right + 1;
        int buttonWidth = Math.Min(_buttonAreaWidth, Math.Max(0, rowBounds.Right - buttonX));
        var buttonBounds = new Rect(buttonX, rowBounds.Y, buttonWidth, 1);
        return new TextInputWithButtonsLayout(
            inputBounds,
            buttonBounds,
            _buttonBar.CalculateLayout(buttonBounds.X, buttonBounds.Y, buttonBounds.Width));
    }

    private readonly record struct TextInputWithButtonsLayout(
        Rect InputBounds,
        Rect ButtonAreaBounds,
        DialogButtonBarLayout ButtonLayout);
}

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

public sealed class ScrollableFormDialog
{
    private IReadOnlyList<IFormRow> _bodyRows = [];
    private IReadOnlyList<IFormRow> _footerRows = [];
    private FormLayoutSnapshot? _stableLayout;
    private FormLayoutSnapshot StableLayout => _stableLayout ?? new(default, default, null, 1, 1, ScrollTop);

    public ScrollableFormDialog()
    {
    }

    public ScrollableFormDialog(IReadOnlyList<IFormRow> rows)
    {
        SetRows(rows);
    }

    public int FocusIndex { get; private set; }
    public int FocusableCount => TotalFocusableCount;
    public int ScrollTop { get; private set; }
    public ScrollBarDragState? ScrollbarDrag { get; private set; }
    public string? FocusedRowId => FocusedRow()?.Id;
    public FormRowRole FocusedRowRole => FocusedRow()?.Role ?? FormRowRole.Normal;
    public bool IsFocusedOnSubmitRow => FocusedRow() is { IsFocusable: true, SubmitOnEnter: true };

    private int BodyRowCount => _bodyRows.Sum(static row => Math.Max(1, row.Height));
    private int FooterRowCount => _footerRows.Sum(static row => Math.Max(1, row.Height));
    private int BodyFocusableCount => _bodyRows.Count(static row => row.IsFocusable);
    private int FooterFocusableCount => _footerRows.Count(static row => row.IsFocusable);
    private int TotalFocusableCount => BodyFocusableCount + FooterFocusableCount;

    public void SetRows(IReadOnlyList<IFormRow> bodyRows, IReadOnlyList<IFormRow>? footerRows = null)
    {
        footerRows ??= [];
        ValidateUniqueIds(bodyRows, footerRows);
        _bodyRows = bodyRows;
        _footerRows = footerRows;
        FocusIndex = ClampFocusIndex(FocusIndex);
        if (TotalFocusableCount > 0 && !IsFocusableAtFocusIndex(FocusIndex))
            FocusIndex = NextFocusableIndex(FocusIndex, 1);
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, BodyRowCount, StableLayout.ViewportRows);
    }

    public bool IsFocused(string rowId) =>
        !string.IsNullOrEmpty(rowId) && string.Equals(FocusedRowId, rowId, StringComparison.Ordinal);

    public bool TryFocus(string rowId)
    {
        int? focusIndex = FindFocusIndexById(rowId);
        if (focusIndex is null)
            return false;

        FocusIndex = focusIndex.Value;
        EnsureFocusVisible(StableLayout.ViewportRows);
        return true;
    }

    public int? FindFocusIndexById(string rowId)
    {
        if (string.IsNullOrEmpty(rowId))
            return null;

        int focusIndex = 0;
        foreach (IFormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (string.Equals(row.Id, rowId, StringComparison.Ordinal))
                return focusIndex;

            focusIndex++;
        }

        return null;
    }

    public void Render(FormRenderContext context)
    {
        if (_footerRows.Count > 0 && context.FooterBounds is null)
            throw new InvalidOperationException("Footer bounds are required when footer rows are installed.");
        if (context.FooterBounds is Rect footerBounds && FooterRowCount > footerBounds.Height)
            throw new InvalidOperationException("Footer rows do not fit within the footer bounds.");

        context.Screen.SetCursorVisible(false);
        int viewportRows = Math.Max(1, context.BodyBounds.Height);
        int effectiveScrollTop = CalculateEffectiveScrollTop(ScrollTop, viewportRows);

        context.Screen.FillRegion(context.BodyBounds, FarDialogStyles.Fill);
        int virtualTop = 0;
        int focusIndex = 0;
        for (int rowIndex = 0; rowIndex < _bodyRows.Count; rowIndex++)
        {
            IFormRow row = _bodyRows[rowIndex];
            int rowHeight = Math.Max(1, row.Height);
            bool visible = virtualTop + rowHeight > effectiveScrollTop && virtualTop < effectiveScrollTop + viewportRows;
            if (visible)
            {
                int y = context.BodyBounds.Y + virtualTop - effectiveScrollTop;
                var rowBounds = new Rect(context.BodyBounds.X, y, context.BodyBounds.Width, rowHeight);
                bool focused = row.IsFocusable && focusIndex == FocusIndex;
                var rowContext = new FormRowRenderContext(context.Screen, rowBounds, focused, context.Viewport.Height);
                row.Render(rowContext);
            }

            if (row.IsFocusable)
                focusIndex++;
            virtualTop += rowHeight;
        }

        if (BodyRowCount > viewportRows)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                context.Screen,
                new Rect(context.BodyBounds.Right - 1, context.BodyBounds.Y, 1, viewportRows),
                new ScrollState
                {
                    TotalItems = BodyRowCount,
                    ViewportItems = viewportRows,
                    FirstVisibleIndex = effectiveScrollTop,
                },
                new ScrollBarOptions
                {
                    Enabled = true,
                    DrawWhenNotScrollable = false,
                },
                context.ScrollbarStyle);
        }

        if (context.FooterBounds is Rect fixedFooterBounds)
        {
            context.Screen.FillRegion(fixedFooterBounds, FarDialogStyles.Fill);
            int footerTop = 0;
            int footerFocusIndex = BodyFocusableCount;
            foreach (IFormRow row in _footerRows)
            {
                int rowHeight = Math.Max(1, row.Height);
                var rowBounds = new Rect(
                    fixedFooterBounds.X,
                    fixedFooterBounds.Y + footerTop,
                    fixedFooterBounds.Width,
                    rowHeight);
                bool focused = row.IsFocusable && footerFocusIndex == FocusIndex;
                row.Render(new FormRowRenderContext(context.Screen, rowBounds, focused, context.Viewport.Height));
                if (row.IsFocusable)
                    footerFocusIndex++;
                footerTop += rowHeight;
            }
        }

        RenderFocusedOverlay(context.Screen, context.BodyBounds, context.FooterBounds, effectiveScrollTop, context.Viewport.Height);

        IFormRow? focusedRow = FocusedRow();
        if (focusedRow is IFormCursorProvider cursorProvider &&
            TryGetFocusedRowBounds(context.BodyBounds, context.FooterBounds, effectiveScrollTop, out Rect focusedBounds) &&
            cursorProvider.TryGetCursor(
                new FormRowRenderContext(context.Screen, focusedBounds, focused: true, screenHeight: context.Viewport.Height),
                out FormCursorPlacement cursor) &&
            cursor.X >= focusedBounds.X && cursor.X < focusedBounds.Right &&
            cursor.Y >= focusedBounds.Y && cursor.Y < focusedBounds.Bottom)
        {
            context.Screen.SetCursorPosition(cursor.X, cursor.Y);
            context.Screen.SetCursorVisible(true);
        }
        else
        {
            context.Screen.SetCursorVisible(false);
        }

        var snapshot = new FormLayoutSnapshot(
            context.Viewport,
            context.BodyBounds,
            context.FooterBounds,
            viewportRows,
            context.Viewport.Height,
            effectiveScrollTop);
        context.PublishOnStable(() =>
        {
            _stableLayout = snapshot;
            ScrollTop = snapshot.EffectiveScrollTop;
        });
    }

    public FormInputResult HandleKey(ConsoleKeyInfo key)
    {
        if (TotalFocusableCount == 0)
            return FormInputResult.NotHandled;

        int availableDropdownRows = TryGetFocusedRowBounds(out Rect focusedBounds)
            ? SingleLineTextInput.AvailableDropdownContentRows(focusedBounds.Y, StableLayout.ScreenHeight)
            : 0;
        IFormRow? focusedRow = FocusedRow();
        FormInputResult rowResult = focusedRow?.HandleKey(
            key,
            new FormRowInputContext(
                FocusIndex,
                focused: true,
                availableDropdownRows,
                focusedRow.Id,
                focusedRow.Role)) ?? FormInputResult.NotHandled;
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
        IFormRow? focusedRow = FocusedRow();
        if (focusedRow is not null && TryGetFocusedRowBounds(out Rect focusedBounds))
        {
            FormInputResult focusedResult = focusedRow.HandleMouse(
                mouse,
                new FormRowMouseContext(
                    focusedBounds,
                    FocusIndex,
                    focused: true,
                    StableLayout.ScreenHeight,
                    focusedRow.Id,
                    focusedRow.Role));
            if (focusedResult.IsHandled)
                return ApplyResult(focusedResult);
        }

        if (TryHandleWheel(mouse, StableLayout.ViewportRows))
            return FormInputResult.Handled;

        if (BodyRowCount > StableLayout.ViewportRows &&
            TryHandleScrollbarMouse(
                mouse,
                new Rect(StableLayout.BodyBounds.Right - 1, StableLayout.BodyBounds.Y, 1, StableLayout.ViewportRows),
                StableLayout.ViewportRows))
        {
            return FormInputResult.Handled;
        }

        if (StableLayout.FooterBounds is Rect footerBounds &&
            TryHitTestRow(
                _footerRows,
                footerBounds,
                scrollTop: 0,
                viewportRows: footerBounds.Height,
                focusIndexOffset: BodyFocusableCount,
                mouse.X,
                mouse.Y,
                out int footerRowIndex,
                out Rect footerRowBounds,
                out int footerFocusIndex))
        {
            return HandleRowMouse(_footerRows[footerRowIndex], footerRowIndex, footerRowBounds, footerFocusIndex, mouse);
        }

        if (!TryHitTestRow(
                _bodyRows,
                StableLayout.BodyBounds,
                ScrollTop,
                StableLayout.ViewportRows,
                focusIndexOffset: 0,
                mouse.X,
                mouse.Y,
                out int rowIndex,
                out Rect rowBounds,
                out int focusIndex))
            return FormInputResult.NotHandled;

        return HandleRowMouse(_bodyRows[rowIndex], rowIndex, rowBounds, focusIndex, mouse);
    }

    private FormInputResult HandleRowMouse(
        IFormRow row,
        int rowIndex,
        Rect rowBounds,
        int focusIndex,
        MouseConsoleInputEvent mouse)
    {
        if (!row.IsFocusable)
        {
            FormInputResult nonFocusableResult = row.HandleMouse(
                mouse,
                new FormRowMouseContext(rowBounds, rowIndex, focused: false, StableLayout.ScreenHeight, row.Id, row.Role));
            return nonFocusableResult.IsHandled ? ApplyResult(nonFocusableResult) : FormInputResult.NotHandled;
        }

        FocusIndex = focusIndex;
        EnsureFocusVisible(StableLayout.ViewportRows);
        FormInputResult result = row.HandleMouse(
            mouse,
            new FormRowMouseContext(rowBounds, FocusIndex, focused: true, StableLayout.ScreenHeight, row.Id, row.Role));
        return result.IsHandled ? ApplyResult(result) : FormInputResult.Handled;
    }

    private void RenderFocusedOverlay(ScreenRenderer screen, Rect bodyBounds, Rect? footerBounds, int scrollTop, int screenHeight)
    {
        if (FocusedRow() is not IFormOverlayRow overlayRow)
            return;

        if (!TryGetFocusedRowBounds(bodyBounds, footerBounds, scrollTop, out Rect bounds))
            return;

        overlayRow.RenderOverlay(new FormRowRenderContext(screen, bounds, focused: true, screenHeight: screenHeight));
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
        ScrollTop = CalculateEffectiveScrollTop(ScrollTop, viewportRows);
    }

    private int CalculateEffectiveScrollTop(int scrollTop, int viewportRows)
    {
        int clampedViewportRows = Math.Max(1, viewportRows);
        int effectiveScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, BodyRowCount, clampedViewportRows);

        int focusVirtualRow = FocusIndexToBodyVirtualRow(FocusIndex);
        if (focusVirtualRow >= 0)
        {
            effectiveScrollTop = ScrollStateCalculator.EnsureIndexVisible(focusVirtualRow, effectiveScrollTop, clampedViewportRows);
            int focusHeight = Math.Max(1, FocusedRow()?.Height ?? 1);
            if (focusHeight <= clampedViewportRows && focusVirtualRow + focusHeight > effectiveScrollTop + clampedViewportRows)
                effectiveScrollTop = focusVirtualRow + focusHeight - clampedViewportRows;
        }

        return ScrollStateCalculator.ClampFirstVisibleIndex(effectiveScrollTop, BodyRowCount, clampedViewportRows);
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
        MoveFocus(delta, StableLayout.ViewportRows);
        return FormInputResult.Handled;
    }

    private FormInputResult MoveFocusPage(int delta)
    {
        if (FocusIndex >= BodyFocusableCount)
        {
            if (delta < 0 && BodyFocusableCount > 0)
                FocusIndex = BodyFocusableCount - 1;
            return FormInputResult.Handled;
        }

        int targetVirtual = Math.Clamp(FocusIndexToBodyVirtualRow(FocusIndex) + delta * StableLayout.ViewportRows, 0, Math.Max(0, BodyRowCount - 1));
        FocusIndex = NearestFocusableIndexAtOrAfterVirtualRow(targetVirtual, delta);
        EnsureFocusVisible(StableLayout.ViewportRows);
        return FormInputResult.Handled;
    }

    private FormInputResult SetFocusResult(int focusIndex)
    {
        FocusIndex = ClampFocusIndex(focusIndex);
        EnsureFocusVisible(StableLayout.ViewportRows);
        return FormInputResult.Handled;
    }

    private IFormRow? FocusedRow()
    {
        int focusIndex = 0;
        foreach (IFormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (focusIndex == FocusIndex)
                return row;

            focusIndex++;
        }

        return null;
    }

    private IEnumerable<IFormRow> AllRows()
    {
        foreach (IFormRow row in _bodyRows)
            yield return row;
        foreach (IFormRow row in _footerRows)
            yield return row;
    }

    private static void ValidateUniqueIds(
        IReadOnlyList<IFormRow> bodyRows,
        IReadOnlyList<IFormRow> footerRows)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (IFormRow row in bodyRows.Concat(footerRows))
        {
            if (!string.IsNullOrEmpty(row.Id) && !ids.Add(row.Id))
                throw new InvalidOperationException($"Duplicate form row ID '{row.Id}'.");
        }
    }

    private bool TryGetFocusedRowBounds(out Rect bounds) =>
        TryGetFocusedRowBounds(StableLayout.BodyBounds, StableLayout.FooterBounds, ScrollTop, out bounds);

    private bool TryGetFocusedRowBounds(Rect bodyBounds, Rect? footerBounds, int scrollTop, out Rect bounds)
    {
        bounds = default;
        int currentFocusIndex = 0;
        int currentVirtual = 0;
        foreach (IFormRow row in _bodyRows)
        {
            int height = Math.Max(1, row.Height);
            if (row.IsFocusable)
            {
                if (currentFocusIndex == FocusIndex)
                {
                    bounds = new Rect(bodyBounds.X, bodyBounds.Y + currentVirtual - scrollTop, bodyBounds.Width, height);
                    return true;
                }

                currentFocusIndex++;
            }

            currentVirtual += height;
        }

        if (footerBounds is not Rect fixedFooterBounds)
            return false;

        currentVirtual = 0;
        foreach (IFormRow row in _footerRows)
        {
            int height = Math.Max(1, row.Height);
            if (row.IsFocusable)
            {
                if (currentFocusIndex == FocusIndex)
                {
                    bounds = new Rect(fixedFooterBounds.X, fixedFooterBounds.Y + currentVirtual, fixedFooterBounds.Width, height);
                    return true;
                }

                currentFocusIndex++;
            }

            currentVirtual += height;
        }

        return false;
    }

    private static bool TryHitTestRow(
        IReadOnlyList<IFormRow> rows,
        Rect contentBounds,
        int scrollTop,
        int viewportRows,
        int focusIndexOffset,
        int x,
        int y,
        out int rowIndex,
        out Rect rowBounds,
        out int focusIndex)
    {
        rowIndex = -1;
        rowBounds = default;
        focusIndex = -1;
        int rowCount = rows.Sum(static row => Math.Max(1, row.Height));
        if (!TryHitTestBodyRow(contentBounds, scrollTop, rowCount, viewportRows, x, y, out int virtualRow))
            return false;

        int currentVirtualRow = 0;
        int currentFocusIndex = focusIndexOffset;
        for (int i = 0; i < rows.Count; i++)
        {
            IFormRow row = rows[i];
            int height = Math.Max(1, row.Height);
            if (virtualRow >= currentVirtualRow && virtualRow < currentVirtualRow + height)
            {
                rowIndex = i;
                rowBounds = new Rect(contentBounds.X, contentBounds.Y + currentVirtualRow - scrollTop, contentBounds.Width, height);
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
        int count = TotalFocusableCount;
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
    private int LastFocusableIndex() => Math.Max(0, TotalFocusableCount - 1);

    private int ClampFocusIndex(int focusRow)
    {
        int count = TotalFocusableCount;
        return count <= 0 ? 0 : Math.Clamp(focusRow, 0, count - 1);
    }

    private bool IsFocusableAtFocusIndex(int focusRow)
    {
        return focusRow >= 0 && focusRow < TotalFocusableCount;
    }

    private int FocusIndexToBodyVirtualRow(int focusIndex)
    {
        int currentFocusRow = 0;
        int virtualRow = 0;
        foreach (IFormRow row in _bodyRows)
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
        for (int i = 0, currentVirtual = 0; i < _bodyRows.Count; i++)
        {
            IFormRow row = _bodyRows[i];
            if (row.IsFocusable)
            {
                if (currentVirtual >= virtualRow)
                    return currentFocusIndex;

                bestBefore = currentFocusIndex;
                currentFocusIndex++;
            }

            currentVirtual += Math.Max(1, row.Height);
        }

        return direction > 0 ? Math.Max(0, BodyFocusableCount - 1) : bestBefore;
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

    private sealed record FormLayoutSnapshot(
        ConsoleViewport Viewport,
        Rect BodyBounds,
        Rect? FooterBounds,
        int ViewportRows,
        int ScreenHeight,
        int EffectiveScrollTop);

    internal static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
