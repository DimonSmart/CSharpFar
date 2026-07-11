using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class ChoiceDialogOptions
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<string> Lines { get; init; } = [];
    public IReadOnlyList<DialogButton> Buttons { get; init; } = [];
    public int DefaultButtonIndex { get; init; }
    public int CancelButtonIndex { get; init; }
}

public readonly record struct ChoiceDialogResult(int ButtonIndex, string ButtonId);

public sealed class ChoiceDialog
{
    private const int MinDialogWidth = 20;
    private const int MaxDialogWidth = 96;

    private readonly ModalDialogHost _modalDialogs;

    public ChoiceDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
    }

    public ChoiceDialogResult Show(ChoiceDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Buttons.Count == 0)
            throw new ArgumentException("At least one button is required.", nameof(options));

        var buttonBar = new DialogButtonBar(options.Buttons);
        int focusedButton = ClampButtonIndex(options.DefaultButtonIndex, options.Buttons);
        int cancelButton = ClampButtonIndex(options.CancelButtonIndex, options.Buttons);
        using var session = _modalDialogs.Open(context =>
        {
            var layout = CreateLayout(options, context.Size);
            var buttons = RenderLayer(context.Screen, options, layout, buttonBar, focusedButton);
            return new ChoiceDialogFrame(layout, buttons);
        });

        while (true)
        {
            session.Render();
            var input = session.ReadInput(out var frame);
            if (buttonBar.TryHandleInput(input, frame.Buttons, ref focusedButton, out string? buttonId))
            {
                if (buttonId is not null)
                    return ResultForButtonId(options.Buttons, buttonId);
                continue;
            }

            if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape })
                return ResultForIndex(options.Buttons, cancelButton);
        }
    }

    private static DialogButtonBarLayout RenderLayer(
        ScreenRenderer screen,
        ChoiceDialogOptions options,
        ChoiceDialogLayout layout,
        DialogButtonBar buttonBar,
        int focusedButton)
    {
        DialogButtonBarLayout buttons = null!;
        var palette = UiTheme.Current;
        new DialogFrameRenderer().RenderFrame(
            screen,
            layout.Bounds,
            options.Title,
            false,
            PaletteStyles.DialogPopupOptions(palette),
            (_, contentBounds) =>
            {
                int textX = contentBounds.X + 1;
                int textWidth = Math.Max(1, contentBounds.Width - 2);
                for (int i = 0; i < layout.LineRows; i++)
                {
                    string line = i < options.Lines.Count ? options.Lines[i] : string.Empty;
                    screen.Write(
                        textX,
                        contentBounds.Y + i,
                        Fit(line, textWidth),
                        PaletteStyles.DialogFill(palette));
                }

                buttons = buttonBar.Render(
                    screen,
                    textX,
                    layout.ButtonY,
                    textWidth,
                    focusedButton,
                    PaletteStyles.DialogFill(palette),
                    PaletteStyles.InputField(palette));
            });
        screen.SetCursorVisible(false);
        return buttons;
    }

    private static ChoiceDialogLayout CreateLayout(ChoiceDialogOptions options, ConsoleSize size)
    {
        int lineWidth = options.Lines.DefaultIfEmpty(string.Empty).Max(line => line.Length);
        int buttonWidth = options.Buttons.Sum(button => button.Text.Length + 4) + Math.Max(0, options.Buttons.Count - 1);
        int titleWidth = string.IsNullOrEmpty(options.Title) ? 0 : options.Title.Length + 2;
        int desiredWidth = Math.Max(MinDialogWidth, Math.Max(Math.Max(lineWidth, buttonWidth), titleWidth) + 4);
        int width = Math.Min(Math.Min(MaxDialogWidth, desiredWidth), Math.Max(1, size.Width));

        int lineRows = Math.Max(1, options.Lines.Count);
        int desiredHeight = lineRows + 4;
        int height = Math.Min(desiredHeight, Math.Max(3, size.Height));
        lineRows = Math.Max(0, height - 4);

        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        return new ChoiceDialogLayout(new Rect(x, y, width, height), lineRows, y + height - 2);
    }

    private static ChoiceDialogResult ResultForButtonId(IReadOnlyList<DialogButton> buttons, string buttonId)
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            if (buttons[i].Id == buttonId)
                return new ChoiceDialogResult(i, buttonId);
        }

        return new ChoiceDialogResult(-1, buttonId);
    }

    private static ChoiceDialogResult ResultForIndex(IReadOnlyList<DialogButton> buttons, int index) =>
        new(index, buttons[index].Id);

    private static int ClampButtonIndex(int index, IReadOnlyList<DialogButton> buttons) =>
        Math.Clamp(index, 0, buttons.Count - 1);

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width
            ? text.PadRight(width)
            : text[..width];
    }

    private readonly record struct ChoiceDialogLayout(Rect Bounds, int LineRows, int ButtonY);

    private readonly record struct ChoiceDialogFrame(ChoiceDialogLayout Layout, DialogButtonBarLayout Buttons);
}
