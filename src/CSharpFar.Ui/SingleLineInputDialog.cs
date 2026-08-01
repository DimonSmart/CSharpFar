using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class SingleLineInputDialogOptions
{
    public string Title { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string InitialText { get; init; } = string.Empty;
    public bool AllowEmpty { get; init; }
    public bool MaskInput { get; init; }
    public TextHistoryId? History { get; init; }
    public Func<string, string?>? Validate { get; init; }
}

public readonly record struct SingleLineInputDialogResult(bool IsConfirmed, string Text);

public sealed class SingleLineInputDialog
{
    private const int DialogWidth = 52;
    private const int DialogHeight = 7;

    private readonly ModalFormHost _formDialogs;
    private readonly FormFieldFactory _fields;

    public SingleLineInputDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _formDialogs = new ModalFormHost(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public SingleLineInputDialogResult Show(SingleLineInputDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return RunLoop(options);
    }

    private SingleLineInputDialogResult RunLoop(SingleLineInputDialogOptions options)
    {
        TextField field = _fields.Text("input", options.InitialText, options.History,
            maskInput: options.MaskInput, submitOnEnter: true);
        string? error = null;
        var actions = new ButtonRow([
            DialogButton.Default("ok", "OK", 'O'),
            DialogButton.Cancel(),
        ])
        { Id = "actions" };
        var form = new ScrollableFormDialog();
        void PrepareRows() => form.SetRows([
            new LabelRow(options.Prompt),
            FormControls.Text(field),
            new SpacerRow(),
        ], FormFooter.ErrorAndButtons(() => error, actions));

        return _formDialogs.Run(
            form,
            new ModalFormOptions(
                options.Title, DialogWidth, DialogHeight, MinWidth: 20, MinHeight: 5, DoubleBorder: false,
                OuterRenderOptions: PaletteStyles.DialogPopupOptions(UiTheme.Current),
                FrameRenderOptions: PaletteStyles.DialogPopupOptions(UiTheme.Current) with { DrawShadow = false }),
            static layout => ModalFormLayout.WithFooter(layout.ContentBounds, footerHeight: 2),
            (routed, result) =>
            {
                if (FormDialogInput.ShouldCancel(result))
                    return ModalDialogLoopResult<SingleLineInputDialogResult>.Complete(new(false, string.Empty));

                if (!FormDialogInput.ShouldSubmit(routed, result, form))
                    return ModalDialogLoopResult<SingleLineInputDialogResult>.ContinueNoChange;

                string text = field.TrimmedText;
                error = text.Length == 0 && !options.AllowEmpty
                    ? "A value is required."
                    : options.Validate?.Invoke(text);
                if (error is not null)
                    return ModalDialogLoopResult<SingleLineInputDialogResult>.ContinueWithFocus(form.GetFocusTarget("input"));

                field.AcceptHistory();
                return ModalDialogLoopResult<SingleLineInputDialogResult>.Complete(new(true, text));
            },
            prepareRender: PrepareRows);
    }
}
