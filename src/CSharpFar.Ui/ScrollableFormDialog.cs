using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using System.Runtime.CompilerServices;

namespace CSharpFar.Ui;

public enum FormInputResultKind
{
    NotHandled,
    Handled,
    OverlayChanged,
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
    public static FormInputResult OverlayChanged => new(FormInputResultKind.OverlayChanged);
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
    bool IsEnabled { get; }
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

public interface IFormTransientOverlayRow : IFormRow
{
    bool IsOverlayOpen { get; }
    void CancelOverlay();
}

public interface IFormHistoryRow : IFormRow
{
    SingleLineTextHistoryState? History { get; }
    TextInputRowState State { get; }
    Rect GetInputBounds(Rect rowBounds);
    bool IsHistoryArrow(MouseConsoleInputEvent mouse, FormRowMouseContext context);
}

public interface IFormDropdownRow : IFormRow
{
    bool IsDropdownOpen { get; }
    Rect GetFieldBounds(Rect rowBounds);
    DropdownSelectFrame BuildDropdownFrame(Rect rowBounds, ConsoleViewport viewport);
    void RenderDropdownOverlay(FormRowRenderContext context, DropdownSelectFrame frame);
    FormInputResult HandleDropdownKey(ConsoleKeyInfo key, FormRowInputContext context, DropdownSelectFrame frame);
    FormInputResult HandleDropdownMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, DropdownSelectFrame frame);
    void CommitDropdownFrame(DropdownSelectFrame frame);
    void CloseDropdown();
}

public readonly record struct FormCursorPlacement(int X, int Y);

public interface IFormCursorProvider
{
    bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor);
}

public enum FormTargetKind
{
    Row,
    BodyScrollbar,
    HistoryDropdown,
    HistoryScrollbar,
    DropdownPopup,
    DropdownScrollbar,
}

internal static class FormTargetIds
{
    public static UiTargetId BodyScrollbar { get; } = new("form.body-scrollbar");

    public static UiTargetId ForExplicitRow(string id) =>
        new($"form.row.id:{Uri.EscapeDataString(id)}");

    public static UiTargetId ForAnonymousRow(long token) =>
        new($"form.row.instance:{token}");

    public static UiTargetId ForHistoryDropdown(UiTargetId rowTarget) =>
        new($"{rowTarget.Value}:history-dropdown");

    public static UiTargetId ForHistoryScrollbar(UiTargetId rowTarget) =>
        new($"{rowTarget.Value}:history-scrollbar");

    public static UiTargetId ForDropdownPopup(UiTargetId rowTarget) =>
        new($"{rowTarget.Value}:dropdown-popup");

    public static UiTargetId ForDropdownScrollbar(UiTargetId rowTarget) =>
        new($"{rowTarget.Value}:dropdown-scrollbar");
}

public sealed record ScrollableFormFrame(
    ConsoleViewport Viewport,
    Rect BodyBounds,
    Rect? FooterBounds,
    int ViewportRows,
    int ScreenHeight,
    int EffectiveScrollTop,
    IReadOnlyList<FormTargetFrame> Targets,
    UiTargetId? DefaultTarget);

public sealed record FormTargetFrame(
    UiTargetId Target,
    FormTargetKind Kind,
    IFormRow? Row,
    int RowIndex,
    int? FocusIndex,
    Rect Bounds,
    Rect? HitBounds,
    bool IsFocusable,
    bool IsFooter,
    UiCursorPlacement? Cursor = null,
    DropdownSelectFrame? DropdownFrame = null);

public readonly record struct FormRouteResult(
    FormInputResult FormResult,
    UiInputResult UiResult);

public static class FormDialogInput
{
    public static bool ShouldImplicitlySubmit(
        UiRoutedInput<ScrollableFormFrame> routed,
        FormInputResult result,
        ScrollableFormDialog form) =>
        result.Kind == FormInputResultKind.NotHandled &&
        routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter } &&
        form.IsFocusedOnSubmitRow;
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
    public void PublishOnStable<T>(T value, Action<T> commit) => _renderContext.PublishOnStable(value, commit);
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
        FormRowRole rowRole = FormRowRole.Normal,
        Rect? bounds = null,
        int screenHeight = 0)
    {
        RowIndex = rowIndex;
        Focused = focused;
        AvailableDropdownContentRows = availableDropdownContentRows;
        RowId = rowId;
        RowRole = rowRole;
        Bounds = bounds;
        ScreenHeight = screenHeight;
    }

    public int RowIndex { get; }
    public bool Focused { get; }
    public int AvailableDropdownContentRows { get; }
    public string? RowId { get; }
    public FormRowRole RowRole { get; }
    public Rect? Bounds { get; }
    public int ScreenHeight { get; }
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
    public virtual bool IsEnabled => true;
    public virtual bool IsFocusable => IsEnabled;
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

public sealed class TextInputRow : FormRow, IFormOverlayRow, IFormCursorProvider, IFormHistoryRow, IFormTransientOverlayRow
{
    private readonly FormTextInputField _field;
    private readonly int? _width;

    public TextInputRow(CommandLineState buffer, SingleLineTextHistoryState? history = null, TextInputRowState? state = null, int? width = null)
    {
        _field = new FormTextInputField(buffer, history, state ?? new TextInputRowState());
        _width = width;
    }

    public CommandLineState Buffer => _field.Buffer;
    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;
    public SingleLineTextHistoryState? History => _field.History;
    public TextInputRowState State => _field.State;
    public int? Width => _width;
    public bool IsOverlayOpen => History?.IsDropdownOpen == true;

    public Rect GetInputBounds(Rect rowBounds) =>
        new(rowBounds.X, rowBounds.Y, Math.Min(rowBounds.Width, _width ?? rowBounds.Width), rowBounds.Height);

    public override void Render(FormRowRenderContext context)
    {
        _field.Render(context, GetInputBounds(context.Bounds));
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        return _field.TryGetCursor(context, GetInputBounds(context.Bounds), out cursor);
    }

    public void RenderOverlay(FormRowRenderContext context)
    {
        _field.RenderOverlay(context, GetInputBounds(context.Bounds));
    }

    public bool IsHistoryArrow(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        return _field.IsHistoryArrow(mouse, GetInputBounds(context.Bounds));
    }

    public void CancelOverlay()
    {
        History?.Close();
        State.HistoryScrollbarDrag = null;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        return _field.HandleKey(key, context);
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        return _field.HandleMouse(mouse, context, GetInputBounds(context.Bounds));
    }
}

public sealed class TextInputRowState
{
    public ScrollBarDragState? HistoryScrollbarDrag;
}

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
        context.Screen.Write(
            context.Bounds.X,
            context.Bounds.Y,
            ScrollableFormDialog.Fit(_label.PadRight(layout.LabelWidth), layout.LabelWidth),
            FarDialogStyles.Fill);
        _dropdown.RenderField(
            context.Screen,
            layout.FieldBounds,
            context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input);
    }

    public void RenderDropdownOverlay(FormRowRenderContext context, DropdownSelectFrame frame) =>
        _dropdown.RenderPopup(context.Screen, frame);

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
            mouse.Kind != MouseEventKind.Down ||
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

    public bool Enabled { get; set; } = true;
    public override bool IsEnabled => Enabled;

    public override void Render(FormRowRenderContext context) =>
        _checkBox.Render(context.Screen, context.Bounds.X, context.Bounds.Y, context.Bounds.Width, context.Focused && Enabled);

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        cursor = new FormCursorPlacement(context.Bounds.X + 1, context.Bounds.Y);
        return context.Focused && context.Bounds.Width >= 3;
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
    private Dictionary<IFormRow, UiTargetId> _targets = new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<IFormRow, AnonymousRowTokenBox> _anonymousRowTokens = new();
    private readonly UiFocusScope _compatFocusScope = new();
    private UiFocusScope? _activeFocusScope;
    private UiTargetId? _requestedInitialTarget;
    private ScrollableFormFrame? _committedFrame;
    private FormLayoutSnapshot? _stableLayout;
    private bool _ensureFocusedTargetVisibleOnNextRender;
    private long _nextAnonymousRowToken;

    private sealed class AnonymousRowTokenBox
    {
        public AnonymousRowTokenBox(long value) => Value = value;
        public long Value { get; }
    }
    private FormLayoutSnapshot StableLayout => _stableLayout ?? new(default, default, null, 1, 1, ScrollTop);

    public ScrollableFormDialog()
    {
    }

    public ScrollableFormDialog(IReadOnlyList<IFormRow> rows)
    {
        SetRows(rows);
    }

    public int FocusIndex => FocusIndexFromScope(ActiveFocusScope.FocusedTarget) ?? 0;
    public int FocusableCount => TotalFocusableCount;
    public int ScrollTop { get; private set; }
    public ScrollBarDragState? ScrollbarDrag { get; private set; }
    public string? FocusedRowId => FocusedTargetFrame()?.Row?.Id;
    public FormRowRole FocusedRowRole => FocusedTargetFrame()?.Row?.Role ?? FormRowRole.Normal;
    public bool IsFocusedOnSubmitRow => FocusedTargetFrame()?.Row is { IsFocusable: true, SubmitOnEnter: true };
    internal UiFocusScope ActiveFocusScope => _activeFocusScope ?? _compatFocusScope;

    private int BodyRowCount => _bodyRows.Sum(static row => Math.Max(1, row.Height));
    private int FooterRowCount => _footerRows.Sum(static row => Math.Max(1, row.Height));
    private int BodyFocusableCount => _bodyRows.Count(static row => row.IsFocusable);
    private int FooterFocusableCount => _footerRows.Count(static row => row.IsFocusable);
    private int TotalFocusableCount => BodyFocusableCount + FooterFocusableCount;

    public void SetRows(IReadOnlyList<IFormRow> bodyRows, IReadOnlyList<IFormRow>? footerRows = null)
    {
        footerRows ??= [];
        ValidateUniqueIds(bodyRows, footerRows);
        UiTargetId? focusedTarget = ActiveFocusScope.FocusedTarget;
        _bodyRows = bodyRows;
        _footerRows = footerRows;
        _targets = CreateTargetMap(bodyRows, footerRows);
        if (focusedTarget is not null && !AllRows().Any(row => row.IsFocusable && RowTarget(row) == focusedTarget))
            _requestedInitialTarget = null;
        _compatFocusScope.Commit(BuildLogicalFocusFrame());
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, BodyRowCount, StableLayout.ViewportRows);
    }

    public bool IsFocused(string rowId) =>
        !string.IsNullOrEmpty(rowId) && string.Equals(FocusedRowId, rowId, StringComparison.Ordinal);

    public bool TryFocus(string rowId)
    {
        if (string.IsNullOrEmpty(rowId))
            return false;

        IFormRow? row = AllRows().FirstOrDefault(value =>
            value.IsFocusable &&
            string.Equals(value.Id, rowId, StringComparison.Ordinal));
        if (row is null)
            return false;

        if (_committedFrame is not null)
            RestoreCommittedComponentState(_committedFrame);

        UiTargetId target = RowTarget(row);
        if (_activeFocusScope is null ||
            ActiveFocusScope.CurrentFrame.Entries.Count == 0 ||
            !ActiveFocusScope.CurrentFrame.Entries.Any(entry => entry.Target == target))
        {
            CancelTransientOverlayExcept(target);
            _requestedInitialTarget = target;
            _compatFocusScope.TryFocus(target);
            _activeFocusScope?.ClearFocus();
            RequestEnsureFocusVisible();
            return true;
        }

        bool focused = ActiveFocusScope.TryFocus(target);
        if (focused)
        {
            CancelTransientOverlayExcept(null);
            RequestEnsureFocusVisible();
            EnsureFocusVisibleNow(StableLayout.ViewportRows);
        }
        return focused;
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

    private UiTargetId RowTarget(IFormRow row) =>
        _targets.TryGetValue(row, out UiTargetId? target)
            ? target
            : throw new InvalidOperationException("Form row is not installed in this dialog.");

    private Dictionary<IFormRow, UiTargetId> CreateTargetMap(
        IReadOnlyList<IFormRow> bodyRows,
        IReadOnlyList<IFormRow> footerRows)
    {
        var targets = new Dictionary<IFormRow, UiTargetId>(ReferenceEqualityComparer.Instance);
        foreach (IFormRow row in bodyRows.Concat(footerRows))
        {
            targets[row] = string.IsNullOrEmpty(row.Id)
                ? FormTargetIds.ForAnonymousRow(AnonymousRowToken(row))
                : FormTargetIds.ForExplicitRow(row.Id);
        }

        return targets;
    }

    private long AnonymousRowToken(IFormRow row)
    {
        return _anonymousRowTokens.GetValue(row, _ => new AnonymousRowTokenBox(++_nextAnonymousRowToken)).Value;
    }

    private int? FocusIndexFromScope(UiTargetId? target)
    {
        if (target is null)
            return null;

        int focusIndex = 0;
        foreach (IFormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (RowTarget(row) == target)
                return focusIndex;

            focusIndex++;
        }

        return null;
    }

    private UiFocusFrame BuildLogicalFocusFrame()
    {
        var entries = AllRows()
            .Where(row => row.IsFocusable)
            .Select((row, index) => new UiFocusEntry(RowTarget(row), index))
            .ToArray();
        UiTargetId? defaultTarget = _requestedInitialTarget;
        if (defaultTarget is null || !entries.Any(entry => entry.Target == defaultTarget))
            defaultTarget = entries.FirstOrDefault()?.Target;
        return new UiFocusFrame(entries, defaultTarget);
    }

    private FormTargetFrame? FocusedTargetFrame()
    {
        UiTargetId? focused = ActiveFocusScope.FocusedTarget;
        if (focused is null)
            return null;

        int focusIndex = 0;
        foreach (IFormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (RowTarget(row) == focused)
                return new FormTargetFrame(focused, FormTargetKind.Row, row, -1, focusIndex, default, null, true, false);

            focusIndex++;
        }

        return null;
    }

    internal ScrollableFormFrame Render(FormRenderContext context) =>
        Render(context, _compatFocusScope);

    public ScrollableFormFrame Render(FormRenderContext context, UiFocusScope focusScope)
    {
        ArgumentNullException.ThrowIfNull(focusScope);
        if (!ReferenceEquals(_activeFocusScope, focusScope) &&
            !focusScope.HasFocus &&
            _activeFocusScope?.FocusedTarget is UiTargetId previousTarget)
        {
            _requestedInitialTarget = previousTarget;
        }
        _activeFocusScope = focusScope;
        if (_footerRows.Count > 0 && context.FooterBounds is null)
            throw new InvalidOperationException("Footer bounds are required when footer rows are installed.");
        if (context.FooterBounds is Rect footerBounds && FooterRowCount > footerBounds.Height)
            throw new InvalidOperationException("Footer rows do not fit within the footer bounds.");

        int viewportRows = Math.Max(1, context.BodyBounds.Height);
        int effectiveScrollTop = ClampScrollTop(ScrollTop, viewportRows);
        ScrollableFormFrame provisionalFrame = BuildFrame(context, effectiveScrollTop);
        UiTargetId? effectiveFocusedTarget = focusScope.ResolveFocusedTarget(BuildInteractionFrame(provisionalFrame).Focus);
        if (_ensureFocusedTargetVisibleOnNextRender)
            effectiveScrollTop = EnsureFocusedTargetVisible(effectiveScrollTop, viewportRows, effectiveFocusedTarget);
        ScrollableFormFrame frame = BuildFrame(context, effectiveScrollTop, effectiveFocusedTarget);
        UiInteractionFrame interactionFrame = BuildInteractionFrame(frame);

        context.Screen.FillRegion(context.BodyBounds, FarDialogStyles.Fill);
        foreach (FormTargetFrame targetFrame in frame.Targets.Where(target => target.Kind == FormTargetKind.Row && !target.IsFooter && IsVisibleInBody(target.Bounds, context.BodyBounds)))
        {
            bool focused = targetFrame.Target == effectiveFocusedTarget;
            targetFrame.Row!.Render(new FormRowRenderContext(context.Screen, targetFrame.Bounds, focused, context.Viewport.Height));
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
            foreach (FormTargetFrame targetFrame in frame.Targets.Where(target => target.Kind == FormTargetKind.Row && target.IsFooter))
            {
                bool focused = targetFrame.Target == effectiveFocusedTarget;
                targetFrame.Row!.Render(new FormRowRenderContext(context.Screen, targetFrame.Bounds, focused, context.Viewport.Height));
            }
        }

        RenderFocusedOverlay(context.Screen, frame, effectiveFocusedTarget);

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
            _committedFrame = frame;
            ScrollTop = snapshot.EffectiveScrollTop;
            _ensureFocusedTargetVisibleOnNextRender = false;
            foreach (FormTargetFrame target in frame.Targets.Where(target => target.Kind == FormTargetKind.Row && target.Row is IFormDropdownRow && target.DropdownFrame is not null))
            {
                ((IFormDropdownRow)target.Row!).CommitDropdownFrame(target.DropdownFrame!.Value);
            }
        });
        context.PublishOnStable(interactionFrame.Focus, focusScope.Commit);
        return frame;
    }

    public UiInteractionFrame BuildInteractionFrame(ScrollableFormFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var focusEntries = frame.Targets
            .Where(target => target is { Kind: FormTargetKind.Row, IsFocusable: true, FocusIndex: not null })
            .OrderBy(target => target.FocusIndex!.Value)
            .Select(target => new UiFocusEntry(target.Target, target.FocusIndex!.Value, IsEnabled: true, target.Cursor))
            .ToArray();
        var hitRegions = frame.Targets
            .Where(target => target.HitBounds is { Width: > 0, Height: > 0 } && (target.Row is null || target.Row.IsEnabled))
            .Select(target => new UiHitRegion(target.Target, target.HitBounds!.Value))
            .ToArray();
        return new UiInteractionFrame(hitRegions, new UiFocusFrame(focusEntries, frame.DefaultTarget));
    }

    private ScrollableFormFrame BuildFrame(
        FormRenderContext context,
        int effectiveScrollTop,
        UiTargetId? overlayTarget = null)
    {
        var targets = new List<FormTargetFrame>();
        int focusIndex = 0;
        int virtualTop = 0;
        for (int rowIndex = 0; rowIndex < _bodyRows.Count; rowIndex++)
        {
            IFormRow row = _bodyRows[rowIndex];
            int rowHeight = Math.Max(1, row.Height);
            bool visible = virtualTop + rowHeight > effectiveScrollTop &&
                virtualTop < effectiveScrollTop + Math.Max(1, context.BodyBounds.Height);
            Rect rowBounds = visible
                ? new Rect(context.BodyBounds.X, context.BodyBounds.Y + virtualTop - effectiveScrollTop, context.BodyBounds.Width, rowHeight)
                : new Rect(context.BodyBounds.X, context.BodyBounds.Y - rowHeight - 1, context.BodyBounds.Width, rowHeight);
            int? rowFocusIndex = row.IsFocusable ? focusIndex : null;
            targets.Add(CreateRowTargetFrame(context.Screen, row, rowIndex, rowFocusIndex, rowBounds, isFooter: false, context.Viewport, context.BodyBounds));
            if (row.IsFocusable)
                focusIndex++;
            virtualTop += rowHeight;
        }

        if (BodyRowCount > Math.Max(1, context.BodyBounds.Height))
        {
            targets.Add(new FormTargetFrame(
                FormTargetIds.BodyScrollbar,
                FormTargetKind.BodyScrollbar,
                Row: null,
                RowIndex: -1,
                FocusIndex: null,
                new Rect(context.BodyBounds.Right - 1, context.BodyBounds.Y, 1, Math.Max(1, context.BodyBounds.Height)),
                Intersect(
                    new Rect(context.BodyBounds.Right - 1, context.BodyBounds.Y, 1, Math.Max(1, context.BodyBounds.Height)),
                    context.BodyBounds),
                IsFocusable: false,
                IsFooter: false));
        }

        if (context.FooterBounds is Rect footerBounds)
        {
            int footerTop = 0;
            for (int rowIndex = 0; rowIndex < _footerRows.Count; rowIndex++)
            {
                IFormRow row = _footerRows[rowIndex];
                int rowHeight = Math.Max(1, row.Height);
                Rect rowBounds = new(footerBounds.X, footerBounds.Y + footerTop, footerBounds.Width, rowHeight);
                int? rowFocusIndex = row.IsFocusable ? focusIndex : null;
                targets.Add(CreateRowTargetFrame(context.Screen, row, rowIndex, rowFocusIndex, rowBounds, isFooter: true, context.Viewport, footerBounds));
                if (row.IsFocusable)
                    focusIndex++;
                footerTop += rowHeight;
            }
        }

        if (overlayTarget is UiTargetId focusedTarget &&
            targets.FirstOrDefault(target => target.Kind == FormTargetKind.Row && target.Target == focusedTarget) is { Row: { } focusedRow } focusedFrame)
        {
            Rect? activeBounds = focusedFrame.HitBounds;
            if (activeBounds is not null)
            {
                AddRowOverlayTargets(targets, focusedRow, focusedFrame.RowIndex, focusedFrame.Bounds,
                    focusedFrame.IsFooter, focusedFrame.FocusIndex, context.Viewport.Height, focusedTarget);
                AddDropdownOverlayTargets(targets, focusedFrame, focusedTarget);
            }
        }

        UiTargetId? defaultTarget = _requestedInitialTarget;
        if (defaultTarget is null || !targets.Any(target => target.Target == defaultTarget && target.IsFocusable))
            defaultTarget = targets.FirstOrDefault(target => target is { Kind: FormTargetKind.Row, IsFocusable: true })?.Target;

        return new ScrollableFormFrame(
            context.Viewport,
            context.BodyBounds,
            context.FooterBounds,
            Math.Max(1, context.BodyBounds.Height),
            context.Viewport.Height,
            effectiveScrollTop,
            targets,
            defaultTarget);
    }

    private FormTargetFrame CreateRowTargetFrame(
        ScreenRenderer screen,
        IFormRow row,
        int rowIndex,
        int? focusIndex,
        Rect bounds,
        bool isFooter,
        ConsoleViewport viewport,
        Rect activeBounds)
    {
        DropdownSelectFrame? dropdownFrame = row is IFormDropdownRow dropdown
            ? dropdown.BuildDropdownFrame(bounds, viewport)
            : null;
        UiCursorPlacement? cursor = null;
        if (row.IsEnabled && row is IFormCursorProvider cursorProvider &&
            cursorProvider.TryGetCursor(new FormRowRenderContext(screen, bounds, focused: true, viewport.Height), out FormCursorPlacement placement) &&
            placement.X >= bounds.X &&
            placement.X < bounds.Right &&
            placement.Y >= bounds.Y &&
            placement.Y < bounds.Bottom &&
            activeBounds.Contains(placement.X, placement.Y))
        {
            cursor = new UiCursorPlacement(placement.X, placement.Y);
        }

        return new FormTargetFrame(
            RowTarget(row),
            FormTargetKind.Row,
            row,
            rowIndex,
            focusIndex,
            bounds,
            Intersect(bounds, activeBounds),
            row.IsFocusable,
            isFooter,
            cursor,
            dropdownFrame);
    }

    private static bool IsVisibleInBody(Rect bounds, Rect bodyBounds) =>
        bounds.Bottom > bodyBounds.Y && bounds.Y < bodyBounds.Bottom;

    private static Rect? Intersect(Rect first, Rect second)
    {
        int left = Math.Max(first.X, second.X);
        int top = Math.Max(first.Y, second.Y);
        int right = Math.Min(first.Right, second.Right);
        int bottom = Math.Min(first.Bottom, second.Bottom);
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : null;
    }

    private static void AddRowOverlayTargets(
        List<FormTargetFrame> targets,
        IFormRow row,
        int rowIndex,
        Rect rowBounds,
        bool isFooter,
        int? focusIndex,
        int screenHeight,
        UiTargetId rowTarget)
    {
        if (row is not IFormHistoryRow textInput || textInput.History is null)
            return;

        Rect inputBounds = textInput.GetInputBounds(rowBounds);
        SingleLineTextHistoryFrame? historyFrame = SingleLineTextInput.CalculateHistoryDropdownFrame(
            inputBounds.X,
            inputBounds.Y,
            inputBounds.Width,
            screenHeight,
            textInput.History);
        if (historyFrame is not { } frame)
            return;

        targets.Add(new FormTargetFrame(
            FormTargetIds.ForHistoryDropdown(rowTarget),
            FormTargetKind.HistoryDropdown,
            row,
            rowIndex,
            focusIndex,
            frame.PopupBounds,
            frame.PopupBounds,
            IsFocusable: false,
            IsFooter: isFooter));
        if (frame.ScrollbarBounds is Rect scrollbarBounds)
        {
            targets.Add(new FormTargetFrame(
                FormTargetIds.ForHistoryScrollbar(rowTarget),
                FormTargetKind.HistoryScrollbar,
                row,
                rowIndex,
                focusIndex,
                scrollbarBounds,
                scrollbarBounds,
                IsFocusable: false,
                IsFooter: isFooter));
        }
    }

    public FormRouteResult RouteInput(
        ConsoleInputEvent input,
        ScrollableFormFrame frame,
        UiInputRouteContext route)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(route);
        _activeFocusScope = route.FocusScope;
        RestoreCommittedComponentState(frame);

        if (input is KeyConsoleInputEvent { Key: var key })
            return RouteKey(key, frame, route);

        if (input is MouseConsoleInputEvent mouse)
            return RouteMouse(mouse, frame, route);

        return new FormRouteResult(FormInputResult.NotHandled, UiInputResult.NotHandled);
    }

    private FormRouteResult RouteKey(ConsoleKeyInfo key, ScrollableFormFrame frame, UiInputRouteContext route)
    {
        bool ensureFocusedTargetVisible = false;
        if (route.RouteKind == UiInputRouteKind.FocusedTarget &&
            route.Target is UiTargetId target &&
            FindRowTarget(frame, target) is { Row: { } row } targetFrame)
        {
            ensureFocusedTargetVisible = IsOffscreenBodyTarget(targetFrame, frame.BodyBounds);
            int availableDropdownRows = SingleLineTextInput.AvailableDropdownContentRows(
                targetFrame.Bounds.Y,
                frame.ScreenHeight);
            var inputContext = new FormRowInputContext(
                    targetFrame.FocusIndex ?? -1,
                    focused: true,
                    availableDropdownRows,
                    row.Id,
                    row.Role,
                    targetFrame.Bounds,
                    frame.ScreenHeight);
            FormInputResult rowResult;
            if (row is IFormDropdownRow dropdown && targetFrame.DropdownFrame is { } dropdownFrame)
            {
                rowResult = dropdown.HandleDropdownKey(key, inputContext, dropdownFrame);
            }
            else
            {
                rowResult = row.HandleKey(key, inputContext);
            }
            if (rowResult.IsHandled)
                return FormResult(rowResult, WithEnsureFocusVisible(FormResultToUi(rowResult, targetFrame.Target), ensureFocusedTargetVisible));
        }

        return key.Key switch
        {
            ConsoleKey.UpArrow => FormResult(FormInputResult.Handled, UiInputResultWithFocus(UiFocusRequest.MovePrevious)),
            ConsoleKey.DownArrow => FormResult(FormInputResult.Handled, UiInputResultWithFocus(UiFocusRequest.MoveNext)),
            ConsoleKey.PageUp => MoveFocusPage(frame, -1),
            ConsoleKey.PageDown => MoveFocusPage(frame, 1),
            ConsoleKey.Home => SetFocusByIndex(frame, 0),
            ConsoleKey.End => SetFocusByIndex(frame, Math.Max(0, TotalFocusableCount - 1)),
            ConsoleKey.Tab when (key.Modifiers & ConsoleModifiers.Shift) != 0 => FormResult(FormInputResult.Handled, UiInputResultWithFocus(UiFocusRequest.MovePrevious)),
            ConsoleKey.Tab => FormResult(FormInputResult.Handled, UiInputResultWithFocus(UiFocusRequest.MoveNext)),
            ConsoleKey.Escape => FormResult(FormInputResult.Cancel(), UiInputResult.HandledResult),
            _ => FormResult(FormInputResult.NotHandled, UiInputResult.NotHandled),
        };
    }

    private FormRouteResult RouteMouse(MouseConsoleInputEvent mouse, ScrollableFormFrame frame, UiInputRouteContext route)
    {
        bool closedOverlay = CloseFocusedHistoryOnOutsideClick(mouse, frame, route) ||
            CloseFocusedDropdownOnOutsideClick(mouse, frame, route);
        if (route.RouteKind == UiInputRouteKind.Layer)
        {
            if (TryHandleWheel(mouse, frame.ViewportRows))
                return MergeTransientOverlayChange(FormInputResult.Handled, UiInputResult.HandledAndInvalidate, closedOverlay);
            return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);
        }

        if (route.Target is not UiTargetId target)
            return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);

        FormTargetFrame? targetFrame = FindTarget(frame, target);
        if (targetFrame is null)
            return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);

        if (targetFrame.Kind == FormTargetKind.BodyScrollbar)
        {
            bool handled = TryHandleScrollbarMouse(mouse, targetFrame.Bounds, frame.ViewportRows);
            if (!handled && TryHandleWheel(mouse, frame.ViewportRows))
                return MergeTransientOverlayChange(FormInputResult.Handled, UiInputResult.HandledAndInvalidate, closedOverlay);
            if (!handled)
                return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);

            UiMouseCaptureRequest capture = mouse is { Kind: MouseEventKind.Down, Button: MouseButton.Left }
                ? UiMouseCaptureRequest.Capture(targetFrame.Target, MouseButton.Left)
                : UiMouseCaptureRequest.None;
            return MergeTransientOverlayChange(
                FormInputResult.Handled,
                new UiInputResult(true, true, UiFocusRequest.None, capture),
                closedOverlay);
        }

        if (targetFrame.Row is null)
            return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);

        FormTargetFrame rowFrame = targetFrame.Kind == FormTargetKind.Row
            ? targetFrame
            : FindPrimaryRowFrame(frame, targetFrame.Row) ?? targetFrame;
        bool requestFocus = rowFrame.IsFocusable &&
            route.RouteKind == UiInputRouteKind.HitTarget &&
            mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Down };
        var mouseContext = new FormRowMouseContext(
                rowFrame.Bounds,
                rowFrame.FocusIndex ?? rowFrame.RowIndex,
                focused: rowFrame.Target == route.FocusScope.FocusedTarget || requestFocus,
                frame.ScreenHeight,
                targetFrame.Row.Id,
                targetFrame.Row.Role);
        FormInputResult rowResult;
        if (targetFrame.Row is IFormDropdownRow dropdown &&
            (targetFrame.DropdownFrame ?? rowFrame.DropdownFrame) is { } dropdownFrame)
        {
            rowResult = dropdown.HandleDropdownMouse(mouse, mouseContext, dropdownFrame);
        }
        else
        {
            rowResult = targetFrame.Row.HandleMouse(mouse, mouseContext);
        }
        if (!rowResult.IsHandled && TryHandleWheel(mouse, frame.ViewportRows))
            return MergeTransientOverlayChange(FormInputResult.Handled, UiInputResult.HandledAndInvalidate, closedOverlay);

        UiInputResult uiResult = FormResultToUi(rowResult, rowFrame.Target);
        if (requestFocus)
        {
            RequestEnsureFocusVisible();
            bool canceledOverlay = CancelTransientOverlayExcept(rowFrame.Target);
            uiResult = new UiInputResult(
                true,
                true,
                UiFocusRequest.Set(rowFrame.Target),
                canceledOverlay ? UiMouseCaptureRequest.Release : uiResult.MouseCaptureRequest);
        }

        if (targetFrame.Kind is FormTargetKind.HistoryScrollbar or FormTargetKind.DropdownScrollbar &&
            rowResult.IsHandled &&
            mouse is { Kind: MouseEventKind.Down, Button: MouseButton.Left })
        {
            uiResult = new UiInputResult(
                true,
                true,
                uiResult.FocusRequest,
                UiMouseCaptureRequest.Capture(targetFrame.Target, MouseButton.Left));
        }

        return MergeTransientOverlayChange(rowResult, uiResult, closedOverlay);
    }

    private static bool CloseFocusedHistoryOnOutsideClick(
        MouseConsoleInputEvent mouse,
        ScrollableFormFrame frame,
        UiInputRouteContext route)
    {
        if (mouse is not { Kind: MouseEventKind.Down, Button: MouseButton.Left } ||
            route.FocusScope.FocusedTarget is not UiTargetId focusedTarget ||
            FindRowTarget(frame, focusedTarget)?.Row is not IFormHistoryRow { History: { IsDropdownOpen: true } history } row)
        {
            return false;
        }

        bool insidePopup = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, row) &&
            target.Kind is FormTargetKind.HistoryDropdown or FormTargetKind.HistoryScrollbar &&
            target.HitBounds is Rect bounds && bounds.Contains(mouse.X, mouse.Y));
        if (insidePopup)
            return false;

        bool onHistoryArrow = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, row) &&
            target.Kind == FormTargetKind.Row &&
            target.HitBounds is Rect bounds &&
            bounds.Contains(mouse.X, mouse.Y) &&
            row.IsHistoryArrow(mouse, new FormRowMouseContext(
                target.Bounds,
                target.FocusIndex ?? target.RowIndex,
                focused: true,
                frame.ScreenHeight,
                row.Id,
                row.Role)));
        if (onHistoryArrow)
            return false;

        history.Close();
        row.State.HistoryScrollbarDrag = null;
        return true;
    }

    private static bool CloseFocusedDropdownOnOutsideClick(
        MouseConsoleInputEvent mouse,
        ScrollableFormFrame frame,
        UiInputRouteContext route)
    {
        if (mouse is not { Kind: MouseEventKind.Down, Button: MouseButton.Left } ||
            route.FocusScope.FocusedTarget is not UiTargetId focusedTarget ||
            FindRowTarget(frame, focusedTarget) is not { Row: IFormDropdownRow dropdown, DropdownFrame: { IsOpen: true } dropdownFrame })
        {
            return false;
        }

        bool insideDropdown = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, dropdown) &&
            target.Kind is FormTargetKind.DropdownPopup or FormTargetKind.DropdownScrollbar &&
            target.HitBounds is Rect bounds &&
            bounds.Contains(mouse.X, mouse.Y));
        if (insideDropdown)
            return false;

        bool onField = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, dropdown) &&
            target.Kind == FormTargetKind.Row &&
            target.HitBounds is Rect bounds &&
            bounds.Contains(mouse.X, mouse.Y) &&
            dropdownFrame.FieldBounds.Contains(mouse.X, mouse.Y));
        if (onField)
            return false;

        dropdown.CloseDropdown();
        return true;
    }

    private static FormRouteResult MergeTransientOverlayChange(
        FormInputResult formResult,
        UiInputResult uiResult,
        bool overlayClosed)
    {
        if (!overlayClosed)
            return FormResult(formResult, uiResult);

        FormInputResult mergedFormResult = formResult.Kind == FormInputResultKind.NotHandled
            ? FormInputResult.OverlayChanged
            : formResult;
        return FormResult(
            mergedFormResult,
            new UiInputResult(
                true,
                true,
                uiResult.FocusRequest,
                uiResult.MouseCaptureRequest.Kind == UiMouseCaptureRequestKind.None
                    ? UiMouseCaptureRequest.Release
                    : uiResult.MouseCaptureRequest));
    }

    private static FormRouteResult FormResult(FormInputResult formResult, UiInputResult uiResult) =>
        new(formResult, uiResult);

    private UiInputResult FormResultToUi(FormInputResult result, UiTargetId sourceTarget)
    {
        return result.Kind switch
        {
            FormInputResultKind.NotHandled => UiInputResult.NotHandled,
            FormInputResultKind.MoveFocusNext => UiInputResultWithFocus(UiFocusRequest.MoveNext),
            FormInputResultKind.MoveFocusPrevious => UiInputResultWithFocus(UiFocusRequest.MovePrevious),
            FormInputResultKind.Handled => UiInputResult.HandledAndInvalidate,
            FormInputResultKind.OverlayChanged => UiInputResult.HandledAndInvalidate,
            _ => UiInputResult.HandledAndInvalidate,
        };
    }

    private UiInputResult UiInputResultWithFocus(UiFocusRequest request)
    {
        bool canceledOverlay = CancelTransientOverlayForFocusRequest(request);
        RequestEnsureFocusVisible();
        return new UiInputResult(
            true,
            true,
            request,
            canceledOverlay ? UiMouseCaptureRequest.Release : UiMouseCaptureRequest.None);
    }

    private UiInputResult WithEnsureFocusVisible(UiInputResult result, bool ensure)
    {
        if (ensure)
            RequestEnsureFocusVisible();
        return result;
    }

    private FormRouteResult MoveFocusPage(ScrollableFormFrame frame, int delta)
    {
        int current = FocusIndex;
        if (current >= BodyFocusableCount)
        {
            if (delta < 0 && BodyFocusableCount > 0)
                return SetFocusByIndex(frame, BodyFocusableCount - 1);
            return FormResult(FormInputResult.Handled, UiInputResult.HandledResult);
        }

        int targetVirtual = Math.Clamp(
            FocusIndexToBodyVirtualRow(current) + delta * frame.ViewportRows,
            0,
            Math.Max(0, BodyRowCount - 1));
        return SetFocusByIndex(frame, NearestFocusableIndexAtOrAfterVirtualRow(targetVirtual, delta));
    }

    private FormRouteResult SetFocusByIndex(ScrollableFormFrame frame, int focusIndex)
    {
        FormTargetFrame? target = frame.Targets.FirstOrDefault(value =>
            value is { Kind: FormTargetKind.Row, IsFocusable: true } &&
            value.FocusIndex == ClampFocusIndex(focusIndex));
        if (target is not null)
            RequestEnsureFocusVisible();
        return target is null
            ? FormResult(FormInputResult.NotHandled, UiInputResult.NotHandled)
            : FormResult(FormInputResult.Handled, UiInputResult.RequestFocus(target.Target));
    }

    private static FormTargetFrame? FindTarget(ScrollableFormFrame frame, UiTargetId target) =>
        frame.Targets.LastOrDefault(value => value.Target == target);

    private static FormTargetFrame? FindRowTarget(ScrollableFormFrame frame, UiTargetId target) =>
        frame.Targets.FirstOrDefault(value => value.Target == target && value.Kind == FormTargetKind.Row);

    private static FormTargetFrame? FindPrimaryRowFrame(ScrollableFormFrame frame, IFormRow row) =>
        frame.Targets.FirstOrDefault(value => ReferenceEquals(value.Row, row) && value.Kind == FormTargetKind.Row);

    // Transitional compatibility adapter for tests that do not use a composition host.
    internal FormInputResult HandleKey(ConsoleKeyInfo key)
    {
        ScrollableFormFrame frame = _committedFrame ?? BuildCompatibilityFrame();

        var input = new KeyConsoleInputEvent(key);
        UiInputRouteContext route = ActiveFocusScope.FocusedTarget is UiTargetId target
            ? UiInputRouteContext.FocusedTarget(ActiveFocusScope, target)
            : UiInputRouteContext.Layer(ActiveFocusScope);
        FormRouteResult result = RouteInput(input, frame, route);
        ApplyCompatibilityUiResult(result.UiResult);
        return result.FormResult;
    }

    // Transitional compatibility adapter for tests that do not use a composition host.
    internal FormInputResult HandleMouse(MouseConsoleInputEvent mouse)
    {
        ScrollableFormFrame frame = _committedFrame ?? BuildCompatibilityFrame();

        UiInputRouteContext route;
        if (ScrollbarDrag is not null)
        {
            route = UiInputRouteContext.CapturedTarget(ActiveFocusScope, FormTargetIds.BodyScrollbar);
        }
        else if (BuildInteractionFrame(frame).TryHitTest(mouse.X, mouse.Y, out UiHitRegion region))
        {
            route = UiInputRouteContext.HitTarget(ActiveFocusScope, region.Target);
        }
        else
        {
            route = UiInputRouteContext.Layer(ActiveFocusScope);
        }

        FormRouteResult result = RouteInput(mouse, frame, route);
        ApplyCompatibilityUiResult(result.UiResult);
        return result.FormResult;
    }

    private ScrollableFormFrame BuildCompatibilityFrame()
    {
        var targets = new List<FormTargetFrame>();
        int focusIndex = 0;
        int y = StableLayout.BodyBounds.Y;
        for (int rowIndex = 0; rowIndex < _bodyRows.Count; rowIndex++)
        {
            IFormRow row = _bodyRows[rowIndex];
            int rowHeight = Math.Max(1, row.Height);
            int? rowFocusIndex = row.IsFocusable ? focusIndex : null;
            Rect bounds = new(StableLayout.BodyBounds.X, y, StableLayout.BodyBounds.Width, rowHeight);
            DropdownSelectFrame? dropdownFrame = row is IFormDropdownRow dropdown
                ? dropdown.BuildDropdownFrame(bounds, StableLayout.Viewport)
                : null;
            targets.Add(new FormTargetFrame(
                RowTarget(row),
                FormTargetKind.Row,
                row,
                rowIndex,
                rowFocusIndex,
                bounds,
                bounds,
                row.IsFocusable,
                IsFooter: false,
                DropdownFrame: dropdownFrame));
            if (row.IsFocusable)
                focusIndex++;
            y += rowHeight;
        }

        Rect? compatibilityFooterBounds = StableLayout.FooterBounds ??
            (_footerRows.Count > 0
                ? new Rect(StableLayout.BodyBounds.X, y, StableLayout.BodyBounds.Width, Math.Max(1, FooterRowCount))
                : null);
        if (compatibilityFooterBounds is Rect footerBounds)
        {
            y = footerBounds.Y;
            for (int rowIndex = 0; rowIndex < _footerRows.Count; rowIndex++)
            {
                IFormRow row = _footerRows[rowIndex];
                int rowHeight = Math.Max(1, row.Height);
                int? rowFocusIndex = row.IsFocusable ? focusIndex : null;
                Rect bounds = new(footerBounds.X, y, footerBounds.Width, rowHeight);
                DropdownSelectFrame? dropdownFrame = row is IFormDropdownRow dropdown
                    ? dropdown.BuildDropdownFrame(bounds, StableLayout.Viewport)
                    : null;
                targets.Add(new FormTargetFrame(
                    RowTarget(row),
                    FormTargetKind.Row,
                    row,
                    rowIndex,
                    rowFocusIndex,
                    bounds,
                    bounds,
                    row.IsFocusable,
                    IsFooter: true,
                    DropdownFrame: dropdownFrame));
                if (row.IsFocusable)
                    focusIndex++;
                y += rowHeight;
            }
        }

        UiTargetId? defaultTarget = targets.FirstOrDefault(target => target.IsFocusable)?.Target;
        return new ScrollableFormFrame(
            StableLayout.Viewport,
            StableLayout.BodyBounds,
            compatibilityFooterBounds,
            StableLayout.ViewportRows,
            StableLayout.ScreenHeight,
            StableLayout.EffectiveScrollTop,
            targets,
            defaultTarget);
    }

    private void ApplyCompatibilityUiResult(UiInputResult result)
    {
        switch (result.FocusRequest.Kind)
        {
            case UiFocusRequestKind.Set:
                CancelTransientOverlayExcept(result.FocusRequest.Target);
                ActiveFocusScope.TryFocus(result.FocusRequest.Target!);
                EnsureFocusVisibleNow(StableLayout.ViewportRows);
                break;
            case UiFocusRequestKind.MoveNext:
                CancelTransientOverlayExcept(null);
                ActiveFocusScope.MoveNext();
                EnsureFocusVisibleNow(StableLayout.ViewportRows);
                break;
            case UiFocusRequestKind.MovePrevious:
                CancelTransientOverlayExcept(null);
                ActiveFocusScope.MovePrevious();
                EnsureFocusVisibleNow(StableLayout.ViewportRows);
                break;
            case UiFocusRequestKind.Clear:
                CancelTransientOverlayExcept(null);
                ActiveFocusScope.ClearFocus();
                EnsureFocusVisibleNow(StableLayout.ViewportRows);
                break;
        }
    }

    private bool CancelTransientOverlayForFocusRequest(UiFocusRequest request)
    {
        return request.Kind switch
        {
            UiFocusRequestKind.Set => CancelTransientOverlayExcept(request.Target),
            UiFocusRequestKind.MoveNext or UiFocusRequestKind.MovePrevious or UiFocusRequestKind.Clear =>
                CancelTransientOverlayExcept(null),
            _ => false,
        };
    }

    private static void RestoreCommittedComponentState(ScrollableFormFrame frame)
    {
        foreach (FormTargetFrame target in frame.Targets)
        {
            if (target.Kind != FormTargetKind.Row ||
                target.Row is not IFormDropdownRow dropdown ||
                target.DropdownFrame is not { } dropdownFrame)
            {
                continue;
            }

            dropdown.CommitDropdownFrame(dropdownFrame);
        }
    }

    private bool CancelTransientOverlayExcept(UiTargetId? retainedTarget)
    {
        bool canceled = false;
        foreach (IFormRow row in AllRows())
        {
            if (row is not IFormTransientOverlayRow overlay || !overlay.IsOverlayOpen)
                continue;

            if (retainedTarget is not null && RowTarget(row) == retainedTarget)
                continue;

            overlay.CancelOverlay();
            canceled = true;
        }

        return canceled;
    }

    private static void RenderFocusedOverlay(ScreenRenderer screen, ScrollableFormFrame frame, UiTargetId? focusedTarget)
    {
        if (focusedTarget is null)
            return;

        FormTargetFrame? targetFrame = FindRowTarget(frame, focusedTarget);
        if (targetFrame?.Row is not { } row)
            return;

        bool overlayPublished = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, row) &&
            target.Kind is FormTargetKind.HistoryDropdown or FormTargetKind.DropdownPopup);
        if (!overlayPublished)
            return;

        var context = new FormRowRenderContext(screen, targetFrame.Bounds, focused: true, screenHeight: frame.ScreenHeight);
        if (row is IFormDropdownRow dropdown && targetFrame.DropdownFrame is { } dropdownFrame)
        {
            dropdown.RenderDropdownOverlay(context, dropdownFrame);
            return;
        }

        if (row is IFormOverlayRow overlayRow)
            overlayRow.RenderOverlay(context);
    }

    private void RequestEnsureFocusVisible() => _ensureFocusedTargetVisibleOnNextRender = true;

    private void EnsureFocusVisibleNow(int viewportRows)
    {
        ScrollTop = EnsureFocusedTargetVisible(ScrollTop, viewportRows, ActiveFocusScope.FocusedTarget);
    }

    private int ClampScrollTop(int scrollTop, int viewportRows)
    {
        int clampedViewportRows = Math.Max(1, viewportRows);
        return ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, BodyRowCount, clampedViewportRows);
    }

    private int EnsureFocusedTargetVisible(int scrollTop, int viewportRows, UiTargetId? focusedTarget)
    {
        int clampedViewportRows = Math.Max(1, viewportRows);
        int effectiveScrollTop = ClampScrollTop(scrollTop, clampedViewportRows);
        int? focusIndex = FocusIndexFromScope(focusedTarget);
        if (focusIndex is null)
            return effectiveScrollTop;

        int focusVirtualRow = FocusIndexToBodyVirtualRow(focusIndex.Value);
        if (focusVirtualRow >= 0)
        {
            effectiveScrollTop = ScrollStateCalculator.EnsureIndexVisible(focusVirtualRow, effectiveScrollTop, clampedViewportRows);
            int focusHeight = Math.Max(1, FocusedRow(focusIndex.Value)?.Height ?? 1);
            if (focusHeight <= clampedViewportRows && focusVirtualRow + focusHeight > effectiveScrollTop + clampedViewportRows)
                effectiveScrollTop = focusVirtualRow + focusHeight - clampedViewportRows;
        }

        return ScrollStateCalculator.ClampFirstVisibleIndex(effectiveScrollTop, BodyRowCount, clampedViewportRows);
    }

    private static bool IsOffscreenBodyTarget(FormTargetFrame target, Rect bodyBounds) =>
        !target.IsFooter &&
        (target.HitBounds is null ||
            target.Bounds.Bottom <= bodyBounds.Y ||
            target.Bounds.Y >= bodyBounds.Bottom);

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

    private IFormRow? FocusedRow(int focusIndex)
    {
        int currentFocusIndex = 0;
        foreach (IFormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (currentFocusIndex == focusIndex)
                return row;

            currentFocusIndex++;
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

    private static void AddDropdownOverlayTargets(
        List<FormTargetFrame> targets,
        FormTargetFrame rowFrame,
        UiTargetId rowTarget)
    {
        if (rowFrame.Row is not IFormDropdownRow ||
            rowFrame.DropdownFrame is not { IsOpen: true } frame)
            return;

        if (frame.PopupBounds is not Rect popupBounds)
            return;

        targets.Add(new FormTargetFrame(
            FormTargetIds.ForDropdownPopup(rowTarget),
            FormTargetKind.DropdownPopup,
            rowFrame.Row,
            rowFrame.RowIndex,
            rowFrame.FocusIndex,
            popupBounds,
            popupBounds,
            IsFocusable: false,
            IsFooter: rowFrame.IsFooter,
            DropdownFrame: frame));

        if (frame.ScrollbarBounds is Rect scrollbarBounds)
        {
            targets.Add(new FormTargetFrame(
                FormTargetIds.ForDropdownScrollbar(rowTarget),
                FormTargetKind.DropdownScrollbar,
                rowFrame.Row,
                rowFrame.RowIndex,
                rowFrame.FocusIndex,
                scrollbarBounds,
                scrollbarBounds,
                IsFocusable: false,
                IsFooter: rowFrame.IsFooter,
                DropdownFrame: frame));
        }
    }

    private int ClampFocusIndex(int focusRow)
    {
        int count = TotalFocusableCount;
        return count <= 0 ? 0 : Math.Clamp(focusRow, 0, count - 1);
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
