using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed record SearchOptionLine(string Id, string Label, bool IsChecked);

public sealed class SearchOptionsDialogOptions
{
    public string Title { get; init; } = "Find";
    public string TextLabel { get; init; } = "Text";
    public string InitialPattern { get; init; } = string.Empty;
    public TextHistoryId? History { get; init; }
    public IReadOnlyList<SearchOptionLine> Options { get; init; } = [];
    public int Width { get; init; } = 56;
    public Func<SearchOptionsDialogState, string?>? Validate { get; init; }
    public Action<SearchOptionsDialogState, string>? NormalizeOptions { get; init; }
}

public sealed class SearchOptionsDialogState
{
    private readonly Dictionary<string, bool> _options;

    internal SearchOptionsDialogState(string pattern, IReadOnlyList<SearchOptionLine> options)
    {
        Pattern = pattern;
        _options = options.ToDictionary(option => option.Id, option => option.IsChecked);
    }

    public string Pattern { get; internal set; }

    public bool GetOption(string id) => _options.TryGetValue(id, out bool value) && value;

    public void SetOption(string id, bool value)
    {
        if (!_options.ContainsKey(id))
            throw new ArgumentException($"Unknown search option '{id}'.", nameof(id));

        _options[id] = value;
    }

    internal IReadOnlyDictionary<string, bool> Options => _options;
}

public sealed class SearchOptionsDialogResult
{
    internal SearchOptionsDialogResult(string pattern, IReadOnlyDictionary<string, bool> options)
    {
        Pattern = pattern;
        Options = options;
    }

    public string Pattern { get; }
    public IReadOnlyDictionary<string, bool> Options { get; }

    public bool GetOption(string id) => Options.TryGetValue(id, out bool value) && value;
}

public sealed class SearchOptionsDialog
{
    private const int MinimumWidth = 40;
    private const int MinimumHeight = 8;

    private readonly ModalFormHost _formDialogs;
    private readonly FormFieldFactory _fields;

    public SearchOptionsDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _formDialogs = new ModalFormHost(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public SearchOptionsDialogResult? Show(SearchOptionsDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return RunLoop(options);
    }

    private SearchOptionsDialogResult? RunLoop(SearchOptionsDialogOptions options)
    {
        TextField pattern = _fields.Text("pattern", options.InitialPattern, options.History, submitOnEnter: true);

        var state = new SearchOptionsDialogState(pattern.Text, options.Options);
        var checkboxes = options.Options
            .Select(option => new CheckBoxRow(new CheckBoxLine(option.Label, option.IsChecked)))
            .ToArray();
        var buttons = new ButtonRow(
            [
                DialogButton.Default("find", "Find", 'F'),
                DialogButton.Cancel(),
            ]);
        var form = new ScrollableFormDialog();
        string? error = null;
        void PrepareRows() => form.SetRows(BuildRows(options, pattern, checkboxes),
            FormFooter.ErrorAndButtons(() => error, buttons));
        return _formDialogs.Run(
            form,
            new ModalFormOptions(
                options.Title, options.Width, options.Options.Count + 8, MinimumWidth, MinimumHeight,
                OuterRenderOptions: PaletteStyles.DialogPopupOptions(UiTheme.Current) with { DrawBorder = false },
                FrameRenderOptions: PaletteStyles.DialogPopupOptions(UiTheme.Current) with { DrawShadow = false }),
            static layout => ModalFormLayout.WithFooter(layout.ContentBounds, footerHeight: 2),
            (routed, result) =>
            {
                if (result.Kind == FormInputResultKind.ValueChanged)
                    SynchronizeOptions(options, state, checkboxes);

                if (FormDialogInput.ShouldCancel(result))
                    return ModalDialogLoopResult<SearchOptionsDialogResult?>.Complete(null);

                if (FormDialogInput.ShouldSubmit(routed, result, form))
                {
                    var accepted = HandleButton(result.Command ?? "find", options, state, pattern, ref error);
                    if (accepted.HasValue)
                    {
                        return ModalDialogLoopResult<SearchOptionsDialogResult?>.Complete(
                            accepted.Value ? CreateResult(state) : null);
                    }
                }

                return ModalDialogLoopResult<SearchOptionsDialogResult?>.ContinueNoChange;
            },
            prepareRender: PrepareRows);
    }

    internal static IReadOnlyList<IFormRow> BuildRows(
        SearchOptionsDialogOptions options,
        TextField pattern,
        IReadOnlyList<CheckBoxRow> checkboxes)
    {
        var rows = new List<IFormRow>
        {
            new LabelRow(options.TextLabel),
            FormControls.Text(pattern),
        };
        rows.AddRange(checkboxes);
        return rows;
    }

    private static void SynchronizeOptions(
        SearchOptionsDialogOptions options,
        SearchOptionsDialogState state,
        IReadOnlyList<CheckBoxRow> checkboxes)
    {
        for (int i = 0; i < checkboxes.Count; i++)
        {
            string optionId = options.Options[i].Id;
            if (state.GetOption(optionId) == checkboxes[i].Value)
                continue;

            state.SetOption(optionId, checkboxes[i].Value);
            options.NormalizeOptions?.Invoke(state, optionId);
            break;
        }

        for (int i = 0; i < checkboxes.Count; i++)
            checkboxes[i].Value = state.GetOption(options.Options[i].Id);
    }

    private static bool? HandleButton(
        string? buttonId,
        SearchOptionsDialogOptions options,
        SearchOptionsDialogState state,
        TextField pattern,
        ref string? error)
    {
        if (buttonId == "cancel")
            return false;

        if (buttonId == "find")
            return TryAccept(options, state, pattern, ref error);

        return null;
    }

    private static bool TryAccept(
        SearchOptionsDialogOptions options,
        SearchOptionsDialogState state,
        TextField pattern,
        ref string? error)
    {
        state.Pattern = pattern.Text;
        if (state.Pattern.Length == 0)
        {
            error = "Search text is required.";
            return false;
        }

        error = options.Validate?.Invoke(state);
        if (error is not null)
            return false;

        pattern.AcceptHistory();
        return true;
    }

    private static SearchOptionsDialogResult CreateResult(SearchOptionsDialogState state) =>
        new(state.Pattern, new Dictionary<string, bool>(state.Options));

}
