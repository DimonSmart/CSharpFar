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
    public string HistoryKey { get; init; } = string.Empty;
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

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();

    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public SearchOptionsDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
    }

    public SearchOptionsDialogResult? Show(SearchOptionsDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return RunLoop(options);
    }

    private SearchOptionsDialogResult? RunLoop(SearchOptionsDialogOptions options)
    {
        var pattern = new CommandLineState();
        if (options.InitialPattern.Length > 0)
            pattern.SetText(options.InitialPattern);

        var state = new SearchOptionsDialogState(pattern.Text, options.Options);
        SingleLineTextHistoryState patternHistory = HistoryRegistry.GetOrCreate(options.HistoryKey);
        var patternRowState = new TextInputRowState();
        var checkboxes = options.Options
            .Select(option => new CheckBoxRow(new CheckBoxLine(option.Label, option.IsChecked)))
            .ToArray();
        var buttons = new ButtonRow(
            [
                new DialogButton("find", "Find", 'F', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C'),
            ],
            FarDialogStyles.Fill,
            FarDialogStyles.FocusedInput);
        var form = new ScrollableFormDialog(BuildRows(options, pattern, patternHistory, patternRowState, checkboxes, buttons));
        string? error = null;
        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, SearchOptionsDialogResult?>(
            (context, focusScope) =>
            {
                var layout = SearchOptionsDialogLayout.Create(context.Size, options.Width, options.Options.Count);
                return Draw(context, focusScope, options, layout, form, error);
            },
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                FormRouteResult result = form.RouteInput(input, frame, route);
                return (result.FormResult, result.UiResult);
            },
            (routed, result) =>
            {
            if (result.Kind == FormInputResultKind.ValueChanged)
                SynchronizeOptions(options, state, checkboxes);

            if (result.Kind == FormInputResultKind.Cancel)
                return ModalDialogLoopResult<SearchOptionsDialogResult?>.Complete(null);

            if (result.Kind == FormInputResultKind.Submit ||
                routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
            {
                string? command = result.Command;
                if (command is null &&
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 or ConsoleKey.Enter })
                {
                    command = "find";
                }

                var accepted = HandleButton(command, options, state, pattern, patternHistory, ref error);
                if (accepted.HasValue)
                {
                    return ModalDialogLoopResult<SearchOptionsDialogResult?>.Complete(
                        accepted.Value ? CreateResult(state) : null);
                }
            }

            return ModalDialogLoopResult<SearchOptionsDialogResult?>.Continue;
            });
    }

    internal static IReadOnlyList<IFormRow> BuildRows(
        SearchOptionsDialogOptions options,
        CommandLineState pattern,
        SingleLineTextHistoryState patternHistory,
        TextInputRowState patternRowState,
        IReadOnlyList<CheckBoxRow> checkboxes,
        ButtonRow buttons)
    {
        var rows = new List<IFormRow>
        {
            new LabelRow(options.TextLabel, FarDialogStyles.Fill),
            new TextInputRow(pattern, patternHistory, patternRowState)
            {
                Id = "pattern",
                Role = FormRowRole.TextInput,
                SubmitOnEnter = true,
            },
        };
        rows.AddRange(checkboxes);
        rows.Add(new SeparatorRow(FarDialogStyles.Fill, drawLine: false));
        rows.Add(buttons);
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
        CommandLineState pattern,
        SingleLineTextHistoryState patternHistory,
        ref string? error)
    {
        if (buttonId == "cancel")
            return false;

        if (buttonId == "find")
            return TryAccept(options, state, pattern, patternHistory, ref error);

        return null;
    }

    private static bool TryAccept(
        SearchOptionsDialogOptions options,
        SearchOptionsDialogState state,
        CommandLineState pattern,
        SingleLineTextHistoryState patternHistory,
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

        patternHistory.Add(state.Pattern);
        return true;
    }

    private static SearchOptionsDialogResult CreateResult(SearchOptionsDialogState state) =>
        new(state.Pattern, new Dictionary<string, bool>(state.Options));

    private ScrollableFormFrame Draw(
        UiRenderContext context,
        UiFocusScope focusScope,
        SearchOptionsDialogOptions options,
        SearchOptionsDialogLayout layout,
        ScrollableFormDialog form,
        string? error)
    {
        var palette = UiTheme.Current;
        ScrollableFormFrame? frame = null;
        _modalRenderer.Render(
            context.Screen,
            layout.Bounds,
            options.Title,
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(palette) with { DrawBorder = false },
            PaletteStyles.DialogPopupOptions(palette) with { DrawShadow = false },
            (_, modalLayout) =>
            {
                Rect content = modalLayout.ContentBounds;
                frame = form.Render(new FormRenderContext(
                    context,
                    new Rect(content.X, content.Y, content.Width, layout.BodyHeight),
                    FarDialogStyles.Border),
                    focusScope);

                string errorText = error is null ? string.Empty : error;
                context.Screen.Write(content.X, layout.ErrorY, ScrollableFormDialog.Fit(errorText, content.Width), PaletteStyles.DialogError(palette));
            });
        return frame ?? throw new InvalidOperationException("Search options dialog did not render a form frame.");
    }

    private readonly record struct SearchOptionsDialogLayout(
        Rect Bounds,
        int BodyHeight,
        int ErrorY)
    {
        public static SearchOptionsDialogLayout Create(ConsoleSize size, int preferredWidth, int optionCount)
        {
            int width = Math.Min(Math.Max(MinimumWidth, preferredWidth), Math.Max(MinimumWidth, size.Width));
            int height = Math.Min(Math.Max(MinimumHeight, optionCount + 8), Math.Max(MinimumHeight, size.Height));
            int x = Math.Max(0, (size.Width - width) / 2);
            int y = Math.Max(0, (size.Height - height) / 2);
            var bounds = new Rect(x, y, width, height);
            int contentY = y + 2;
            int bodyHeight = Math.Max(1, optionCount + 4);
            return new SearchOptionsDialogLayout(
                bounds,
                bodyHeight,
                contentY + bodyHeight);
        }
    }
}
