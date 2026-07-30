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

    private readonly ModalFormHost _formDialogs;
    private readonly SingleLineTextHistoryRegistry _historyRegistry;

    public SingleLineInputDialog(ModalDialogHost modalDialogs, SingleLineTextHistoryRegistry? historyRegistry = null)
    {
        _formDialogs = new ModalFormHost(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));
        _historyRegistry = historyRegistry ?? new SingleLineTextHistoryRegistry();
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
            ? _historyRegistry.GetOrCreate(options.HistoryKey)
            : null;
        string? error = null;
        var actions = new ButtonRow([
            new DialogButton("ok", "OK", 'O', IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
        ])
        { Id = "actions" };
        var form = new ScrollableFormDialog();
        void PrepareRows() => form.SetRows([
            new LabelRow(options.Prompt, FarDialogStyles.Fill),
            new TextInputRow(buffer, history, maskInput: options.MaskInput) { Id = "input", SubmitOnEnter = true },
            new SeparatorRow(FarDialogStyles.Fill, drawLine: false),
        ], [
            new LabelRow(error ?? string.Empty, PaletteStyles.DialogError(UiTheme.Current)),
            actions,
        ]);

        return _formDialogs.Run(
            form,
            new ModalFormOptions(
                options.Title, DialogWidth, DialogHeight, MinWidth: 20, MinHeight: 5, DoubleBorder: false,
                OuterRenderOptions: PaletteStyles.DialogPopupOptions(UiTheme.Current),
                FrameRenderOptions: PaletteStyles.DialogPopupOptions(UiTheme.Current) with { DrawShadow = false }),
            static layout => new ModalFormLayout(new Rect(layout.ContentBounds.X, layout.ContentBounds.Y, layout.ContentBounds.Width, 3),
                new Rect(layout.ContentBounds.X, layout.ContentBounds.Y + 3, layout.ContentBounds.Width, 2)),
            (routed, result) =>
            {
                if (result.Kind == FormInputResultKind.Cancel || result.Command == "cancel")
                    return ModalDialogLoopResult<SingleLineInputDialogResult>.Complete(new(false, string.Empty));

                bool submit = result.Command == "ok" ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form);
                if (!submit)
                    return ModalDialogLoopResult<SingleLineInputDialogResult>.ContinueNoChange;

                string text = buffer.Text.Trim();
                error = text.Length == 0 && !options.AllowEmpty
                    ? "A value is required."
                    : options.Validate?.Invoke(text);
                if (error is not null)
                    return ModalDialogLoopResult<SingleLineInputDialogResult>.ContinueWithFocus(form.GetFocusTarget("input"));

                history?.Add(text);
                return ModalDialogLoopResult<SingleLineInputDialogResult>.Complete(new(true, text));
            },
            prepareRender: PrepareRows);
    }
}
