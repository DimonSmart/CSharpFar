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

        var palette = UiTheme.Current;
        var actions = new DialogActionController(
            options.Buttons,
            options.DefaultButtonIndex,
            options.CancelButtonIndex,
            PaletteStyles.DialogFill(palette),
            PaletteStyles.InputField(palette));
        return _modalDialogs.RunInteractive<ScrollableFormFrame, DialogActionOutcome?, ChoiceDialogResult>(
            (context, focusScope) =>
            {
                var layout = CreateLayout(options, context.Size);
                return RenderLayer(context, focusScope, actions, options, layout);
            },
            actions.BuildInteractionFrame,
            (input, frame, route) =>
            {
                FormRouteResult result = actions.RouteInput(input, frame, route);
                return (actions.Interpret(result.FormResult), result.UiResult);
            },
            (_, outcome) =>
            {
                if (outcome is { } action)
                    return ModalDialogLoopResult<ChoiceDialogResult>.Complete(new ChoiceDialogResult(action.ButtonIndex, action.ButtonId!));
                return ModalDialogLoopResult<ChoiceDialogResult>.Continue;
            });
    }

    private static ScrollableFormFrame RenderLayer(
        UiRenderContext context,
        IUiFocusState focusScope,
        DialogActionController actions,
        ChoiceDialogOptions options,
        ChoiceDialogLayout layout)
    {
        ScrollableFormFrame? frame = null;
        IUiCanvas screen = context.Canvas;
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

                frame = actions.Render(
                    new FormRenderContext(
                        context,
                        new Rect(textX, Math.Max(contentBounds.Y, layout.ButtonY - 1), textWidth, 1),
                        PaletteStyles.DialogBorder(palette),
                        new Rect(textX, layout.ButtonY, textWidth, 1)),
                    focusScope);
            });
        return frame ?? throw new InvalidOperationException("Choice dialog did not render its form frame.");
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

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width
            ? text.PadRight(width)
            : text[..width];
    }

    private readonly record struct ChoiceDialogLayout(Rect Bounds, int LineRows, int ButtonY);
}
