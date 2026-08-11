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

    private readonly FormDialogs _forms;
    private readonly FormFieldFactory _fields;

    public SearchOptionsDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _forms = new FormDialogs(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public SearchOptionsDialogResult? Show(SearchOptionsDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        TextField pattern = _fields.Text(new TextFieldOptions(
            options.InitialPattern,
            options.History,
            SubmitOnEnter: true));
        var state = new SearchOptionsDialogState(pattern.Text, options.Options);
        CheckBoxRow[] checkboxes = options.Options
            .Select(option => FormControls.CheckBox(option.Label, option.IsChecked))
            .ToArray();
        ButtonRow buttons = FormControls.Buttons(
            DialogButton.Default("find", "Find", 'F'),
            DialogButton.Cancel());

        return _forms.Show(
            new FormDialogOptions(
                options.Title,
                PreferredWidth: options.Width,
                MinWidth: MinimumWidth,
                MinHeight: MinimumHeight),
            rows: () => BuildRows(options, pattern, checkboxes),
            footer: () => [buttons],
            valueChanged: formEvent => SynchronizeOption(options, state, checkboxes, formEvent),
            submit: () => TryAccept(options, state, pattern));
    }

    internal static IReadOnlyList<FormRow> BuildRows(
        SearchOptionsDialogOptions options,
        TextField pattern,
        IReadOnlyList<CheckBoxRow> checkboxes)
    {
        var rows = new List<FormRow>
        {
            FormControls.Label(options.TextLabel),
            FormControls.Text(pattern),
        };
        rows.AddRange(checkboxes);
        return rows;
    }

    private static void SynchronizeOption(
        SearchOptionsDialogOptions options,
        SearchOptionsDialogState state,
        IReadOnlyList<CheckBoxRow> checkboxes,
        FormDialogEvent formEvent)
    {
        for (int i = 0; i < checkboxes.Count; i++)
        {
            if (!formEvent.IsValueChangedFrom(checkboxes[i]))
                continue;

            string optionId = options.Options[i].Id;
            state.SetOption(optionId, checkboxes[i].Value);
            options.NormalizeOptions?.Invoke(state, optionId);

            for (int j = 0; j < checkboxes.Count; j++)
                checkboxes[j].Value = state.GetOption(options.Options[j].Id);
            return;
        }
    }

    private static FormSubmitResult<SearchOptionsDialogResult> TryAccept(
        SearchOptionsDialogOptions options,
        SearchOptionsDialogState state,
        TextField pattern)
    {
        state.Pattern = pattern.Text;
        if (state.Pattern.Length == 0)
            return FormSubmit.Invalid<SearchOptionsDialogResult>("Search text is required.", pattern);

        string? error = options.Validate?.Invoke(state);
        return error is null
            ? FormSubmit.Success(CreateResult(state))
            : FormSubmit.Invalid<SearchOptionsDialogResult>(error, pattern);
    }

    private static SearchOptionsDialogResult CreateResult(SearchOptionsDialogState state) =>
        new(state.Pattern, new Dictionary<string, bool>(state.Options));
}
