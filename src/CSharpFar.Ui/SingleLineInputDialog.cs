using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class SingleLineInputDialogOptions
{
    public string Title { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string InitialText { get; init; } = string.Empty;
    public bool AllowEmpty { get; init; }
    public bool MaskInput { get; init; }
    public string? HistoryKey { get; init; }
    public Func<string, string?>? Validate { get; init; }
}

public readonly record struct SingleLineInputDialogResult(bool IsConfirmed, string Text);

public sealed class SingleLineInputDialog
{
    private const int DialogWidth = 52;
    private const int DialogHeight = 7;

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();

    private readonly ModalDialogHost _modalDialogs;
    private readonly ScreenRenderer _screen;
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("ok", "OK", 'O', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public SingleLineInputDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
        _screen = modalDialogs.Screen;
    }

    public SingleLineInputDialogResult Show(SingleLineInputDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return RunLoop(options);
    }

    private SingleLineInputDialogResult RunLoop(SingleLineInputDialogOptions options)
    {
        var buffer = new CommandLineState();
        if (options.InitialText.Length > 0)
            buffer.SetText(options.InitialText);

        SingleLineTextHistoryState? history = options is { MaskInput: false, HistoryKey: not null }
            ? HistoryRegistry.GetOrCreate(options.HistoryKey)
            : null;
        ScrollBarDragState? historyScrollbarDrag = null;
        string? error = null;
        int focusedButton = 0;
        bool buttonsFocused = false;
        SingleLineInputLayout layout = default;
        ConsoleSize size = default;
        using var session = _modalDialogs.Open(context =>
        {
            size = context.Size;
            layout = CreateLayout(size);
            Draw(options, layout, buffer, error, history, focusedButton, buttonsFocused);
        });

        while (true)
        {
            session.Render();
            int availableRows = SingleLineTextInput.AvailableDropdownContentRows(layout.InputY, size.Height);
            var input = session.ReadInput();
            if (input is MouseConsoleInputEvent mouse && history is not null)
            {
                if (SingleLineTextInput.TryHandleHistoryDropdownMouse(
                        history,
                        buffer,
                        mouse,
                        layout.InputX,
                        layout.InputY,
                        layout.InputWidth,
                        size.Height,
                        ref historyScrollbarDrag) ||
                    (SingleLineTextInput.IsHistoryArrowHit(layout.InputX, layout.InputWidth, layout.InputY, mouse.X, mouse.Y) &&
                     SingleLineTextInput.TryOpenHistoryDropdown(history, layout.InputY, size.Height)))
                {
                    buttonsFocused = false;
                    continue;
                }
            }

            if (input is MouseConsoleInputEvent &&
                _buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                buttonsFocused = true;
                if (buttonId == "cancel")
                    return new SingleLineInputDialogResult(false, string.Empty);
                if (buttonId == "ok" && TryAccept(options, buffer, history, ref error, out var result))
                    return result;
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

            if (history?.IsDropdownOpen == true &&
                key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape)
            {
                SingleLineTextInput.HandleKey(buffer, key, ref error, history, availableRows);
                buttonsFocused = false;
                continue;
            }

            if (key.Key == ConsoleKey.Escape)
                return new SingleLineInputDialogResult(false, string.Empty);

            if (key.Key == ConsoleKey.Tab)
            {
                buttonsFocused = !buttonsFocused;
                continue;
            }

            if (buttonsFocused)
            {
                if (_buttonBar.TryHandleInput(input, ref focusedButton, out string? focusedButtonId))
                {
                    if (focusedButtonId == "cancel")
                        return new SingleLineInputDialogResult(false, string.Empty);
                    if (focusedButtonId == "ok" && TryAccept(options, buffer, history, ref error, out var result))
                        return result;
                }

                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                if (TryAccept(options, buffer, history, ref error, out var result))
                    return result;
                continue;
            }

            SingleLineTextInput.HandleKey(buffer, key, ref error, history, availableRows);
        }
    }

    private static bool TryAccept(
        SingleLineInputDialogOptions options,
        CommandLineState buffer,
        SingleLineTextHistoryState? history,
        ref string? error,
        out SingleLineInputDialogResult result)
    {
        result = default;
        string text = buffer.Text.Trim();
        if (text.Length == 0 && !options.AllowEmpty)
            return false;

        error = options.Validate?.Invoke(text);
        if (error is not null)
            return false;

        history?.Add(text);
        result = new SingleLineInputDialogResult(true, text);
        return true;
    }

    private void Draw(
        SingleLineInputDialogOptions options,
        SingleLineInputLayout layout,
        CommandLineState buffer,
        string? error,
        SingleLineTextHistoryState? history,
        int focusedButton,
        bool buttonsFocused)
    {
        var palette = UiTheme.Current;
        new DialogFrameRenderer().RenderFrame(
            _screen,
            layout.Bounds,
            options.Title,
            false,
            PaletteStyles.DialogPopupOptions(palette),
            (_, _) =>
            {
                _screen.Write(
                    layout.InputX,
                    layout.Bounds.Y + 1,
                    Truncate(options.Prompt, layout.InputWidth).PadRight(layout.InputWidth),
                    PaletteStyles.DialogFill(palette));

                SingleLineTextInput.Render(
                    _screen,
                    layout.InputX,
                    layout.InputY,
                    layout.InputWidth,
                    buffer,
                    PaletteStyles.InputField(palette),
                    PaletteStyles.InputHighlight(palette),
                    history,
                    maskInput: options.MaskInput);

                string errorText = error is not null
                    ? Truncate(error, layout.InputWidth).PadRight(layout.InputWidth)
                    : new string(' ', layout.InputWidth);
                _screen.Write(layout.InputX, layout.Bounds.Y + 3, errorText, PaletteStyles.DialogError(palette));

                _buttonBar.Render(
                    _screen,
                    layout.InputX,
                    layout.Bounds.Y + layout.Bounds.Height - 2,
                    layout.InputWidth,
                    buttonsFocused ? focusedButton : -1,
                    PaletteStyles.DialogFill(palette),
                    PaletteStyles.InputField(palette));

                if (!buttonsFocused)
                {
                    int textWidth = history is null ? layout.InputWidth : Math.Max(1, layout.InputWidth - 1);
                    int cursorX = Math.Min(layout.InputX + textWidth - 1, SingleLineTextInput.GetCursorX(layout.InputX, textWidth, buffer));
                    _screen.SetCursorPosition(cursorX, layout.InputY);
                    _screen.SetCursorVisible(true);
                }
                else
                {
                    _screen.SetCursorVisible(false);
                }
            });
    }

    private static SingleLineInputLayout CreateLayout(ConsoleSize size)
    {
        int width = Math.Min(DialogWidth, Math.Max(20, size.Width));
        int height = Math.Min(DialogHeight, Math.Max(5, size.Height));
        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        int inputWidth = Math.Max(1, width - 4);
        return new SingleLineInputLayout(new Rect(x, y, width, height), x + 2, y + 2, inputWidth);
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..Math.Max(0, maxLen - 1)] + "\u2026";

    private readonly record struct SingleLineInputLayout(Rect Bounds, int InputX, int InputY, int InputWidth);
}
