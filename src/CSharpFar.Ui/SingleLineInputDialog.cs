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
    private readonly ModalDialogRenderer _modalRenderer = new();

    public SingleLineInputDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
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
        string? error = null;
        var form = new ScrollableFormDialog([
            new LabelRow(options.Prompt, FarDialogStyles.Fill),
            new TextInputRow(buffer, history, maskInput: options.MaskInput)
            {
                Id = "input",
                SubmitOnEnter = true,
            },
            new SeparatorRow(FarDialogStyles.Fill, drawLine: false),
            new ButtonRow([
                new DialogButton("ok", "OK", 'O', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C'),
            ], FarDialogStyles.Fill, FarDialogStyles.FocusedInput) { Id = "actions" },
        ]);

        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, SingleLineInputDialogResult>(
            (context, focusScope) => Draw(context, focusScope, options.Title, form, error),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                FormRouteResult routed = form.RouteInput(input, frame, route);
                return (routed.FormResult, routed.UiResult);
            },
            (routed, result) =>
            {
                if (result.Kind == FormInputResultKind.Cancel || result.Command == "cancel")
                    return ModalDialogLoopResult<SingleLineInputDialogResult>.Complete(new(false, string.Empty));

                bool submit = result.Command == "ok" ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form);
                if (!submit)
                    return ModalDialogLoopResult<SingleLineInputDialogResult>.Continue;

                string text = buffer.Text.Trim();
                error = text.Length == 0 && !options.AllowEmpty
                    ? "A value is required."
                    : options.Validate?.Invoke(text);
                if (error is not null)
                    return ModalDialogLoopResult<SingleLineInputDialogResult>.ContinueWithFocus(form.GetFocusTarget("input"));

                history?.Add(text);
                return ModalDialogLoopResult<SingleLineInputDialogResult>.Complete(new(true, text));
            });
    }

    private static SingleLineInputLayout CreateLayout(ConsoleSize size)
    {
        int width = Math.Min(DialogWidth, Math.Max(20, size.Width));
        int height = Math.Min(DialogHeight, Math.Max(5, size.Height));
        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        return new SingleLineInputLayout(new Rect(x, y, width, height));
    }

    private ScrollableFormFrame Draw(
        UiRenderContext context,
        IUiFocusState focusScope,
        string title,
        ScrollableFormDialog form,
        string? error)
    {
        SingleLineInputLayout layout = CreateLayout(context.Size);
        ScrollableFormFrame? frame = null;
        var palette = UiTheme.Current;
        _modalRenderer.Render(
            context.Canvas,
            layout.Bounds,
            title,
            doubleBorder: false,
            PaletteStyles.DialogPopupOptions(palette),
            PaletteStyles.DialogPopupOptions(palette) with { DrawShadow = false },
            (_, modalLayout) =>
            {
                Rect content = modalLayout.ContentBounds;
                frame = form.Render(
                    new FormRenderContext(context, new Rect(content.X, content.Y, content.Width, 4), FarDialogStyles.Border),
                    focusScope);
                context.Canvas.Write(content.X, content.Y + 4, ScrollableFormDialog.Fit(error ?? string.Empty, content.Width), PaletteStyles.DialogError(palette));
            });
        return frame ?? throw new InvalidOperationException("Single-line input dialog did not render a form frame.");
    }

    private readonly record struct SingleLineInputLayout(Rect Bounds);
}
