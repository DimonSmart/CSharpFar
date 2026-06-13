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
    private const int ButtonFocusOffset = 1;

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("find", "Find", 'F', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public SearchOptionsDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public SearchOptionsDialogResult? Show(SearchOptionsDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(size, options);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private SearchOptionsDialogResult? RunLoop(ConsoleSize size, SearchOptionsDialogOptions options)
    {
        var pattern = new CommandLineState();
        if (options.InitialPattern.Length > 0)
            pattern.SetText(options.InitialPattern);

        var state = new SearchOptionsDialogState(pattern.Text, options.Options);
        SingleLineTextHistoryState patternHistory = HistoryRegistry.GetOrCreate(options.HistoryKey);
        ScrollBarDragState? historyScrollbarDrag = null;
        var checkboxes = options.Options.Select(option => new CheckBoxLine(option.Label, option.IsChecked)).ToArray();
        int focusRow = 0;
        int focusedButton = 0;
        string? error = null;
        var layout = SearchOptionsDialogLayout.Create(size, options.Width, options.Options.Count);
        int buttonFocusRow = options.Options.Count + ButtonFocusOffset;

        while (true)
        {
            Draw(options, layout, pattern, patternHistory, checkboxes, focusRow, focusedButton, error);
            var input = _screen.ReadInput();
            int availableRows = SingleLineTextInput.AvailableDropdownContentRows(layout.InputY, size.Height);

            if (input is MouseConsoleInputEvent mouse)
            {
                if (SingleLineTextInput.TryHandleHistoryDropdownMouse(
                        patternHistory,
                        pattern,
                        mouse,
                        layout.InputX,
                        layout.InputY,
                        layout.InputWidth,
                        size.Height,
                        ref historyScrollbarDrag) ||
                    (SingleLineTextInput.IsHistoryArrowHit(layout.InputX, layout.InputWidth, layout.InputY, mouse.X, mouse.Y) &&
                     SingleLineTextInput.TryOpenHistoryDropdown(patternHistory, layout.InputY, size.Height)))
                {
                    focusRow = 0;
                    error = null;
                    continue;
                }

                bool handledOptionMouse = false;
                for (int i = 0; i < checkboxes.Length; i++)
                {
                    if (!checkboxes[i].TryHandleMouse(mouse))
                        continue;

                    focusRow = i + 1;
                    SetStateOption(options, state, checkboxes, i);
                    handledOptionMouse = true;
                    break;
                }

                if (handledOptionMouse)
                    continue;

                if (_buttonBar.TryHandleInput(input, ref focusedButton, out string? mouseButtonId))
                {
                    focusRow = buttonFocusRow;
                    var accepted = HandleButton(mouseButtonId, options, state, pattern, patternHistory, ref error);
                    if (accepted.HasValue)
                        return accepted.Value ? CreateResult(state) : null;

                    continue;
                }
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

            if (focusRow == 0)
            {
                if (patternHistory.IsDropdownOpen &&
                    key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape)
                {
                    SingleLineTextInput.HandleKey(pattern, key, ref error, patternHistory, availableRows);
                    continue;
                }

                if (key.Key == ConsoleKey.Tab || key.Key == ConsoleKey.DownArrow)
                {
                    focusRow = NextFocusRow(focusRow, buttonFocusRow, key);
                    continue;
                }

                if (SingleLineTextInput.HandleKey(pattern, key, ref error, patternHistory, availableRows) != TextInputKeyResult.Ignored)
                    continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return null;
                case ConsoleKey.UpArrow:
                    focusRow = Math.Max(0, focusRow - 1);
                    break;
                case ConsoleKey.DownArrow:
                    focusRow = Math.Min(buttonFocusRow, focusRow + 1);
                    break;
                case ConsoleKey.Tab:
                    focusRow = NextFocusRow(focusRow, buttonFocusRow, key);
                    break;
                case ConsoleKey.Spacebar:
                case ConsoleKey.Enter:
                    if (focusRow is > 0 && focusRow < buttonFocusRow)
                    {
                        int optionIndex = focusRow - 1;
                        checkboxes[optionIndex].TryHandleKey(key);
                        SetStateOption(options, state, checkboxes, optionIndex);
                        break;
                    }

                    if (focusRow == buttonFocusRow)
                    {
                        if (_buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
                        {
                            var accepted = HandleButton(buttonId, options, state, pattern, patternHistory, ref error);
                            if (accepted.HasValue)
                                return accepted.Value ? CreateResult(state) : null;
                        }

                        break;
                    }

                    if (TryAccept(options, state, pattern, patternHistory, ref error))
                        return CreateResult(state);
                    break;
            }
        }
    }

    private static int NextFocusRow(int focusRow, int buttonFocusRow, ConsoleKeyInfo key)
    {
        bool backward = key.Key == ConsoleKey.Tab && (key.Modifiers & ConsoleModifiers.Shift) != 0;
        return backward
            ? focusRow <= 0 ? buttonFocusRow : focusRow - 1
            : focusRow >= buttonFocusRow ? 0 : focusRow + 1;
    }

    private static void SetStateOption(
        SearchOptionsDialogOptions options,
        SearchOptionsDialogState state,
        IReadOnlyList<CheckBoxLine> checkboxes,
        int optionIndex)
    {
        state.SetOption(options.Options[optionIndex].Id, checkboxes[optionIndex].Value);
        options.NormalizeOptions?.Invoke(state, options.Options[optionIndex].Id);

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

    private void Draw(
        SearchOptionsDialogOptions options,
        SearchOptionsDialogLayout layout,
        CommandLineState pattern,
        SingleLineTextHistoryState patternHistory,
        IReadOnlyList<CheckBoxLine> checkboxes,
        int focusRow,
        int focusedButton,
        string? error)
    {
        var palette = UiTheme.Current;
        using var frame = _screen.BeginFrame();

        _modalRenderer.Render(
            _screen,
            layout.Bounds,
            options.Title,
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(palette) with { DrawBorder = false },
            PaletteStyles.DialogPopupOptions(palette) with { DrawShadow = false },
            (_, modalLayout) =>
            {
                Rect content = modalLayout.ContentBounds;
                _screen.Write(content.X, content.Y, Fit(options.TextLabel, layout.LabelWidth), PaletteStyles.DialogFill(palette));
                SingleLineTextInput.Render(
                    _screen,
                    layout.InputX,
                    layout.InputY,
                    layout.InputWidth,
                    pattern,
                    focusRow == 0 ? PaletteStyles.InputField(palette) : PaletteStyles.DialogFill(palette),
                    PaletteStyles.InputHighlight(palette),
                    patternHistory);

                for (int i = 0; i < checkboxes.Count; i++)
                    checkboxes[i].Render(_screen, content.X, content.Y + 2 + i, content.Width, focusRow == i + 1);

                string errorText = error is null ? string.Empty : error;
                _screen.Write(content.X, layout.ErrorY, Fit(errorText, content.Width), PaletteStyles.DialogError(palette));
                _buttonBar.Render(
                    _screen,
                    content.X,
                    layout.ButtonY,
                    content.Width,
                    focusRow == checkboxes.Count + ButtonFocusOffset ? focusedButton : -1,
                    PaletteStyles.DialogFill(palette),
                    PaletteStyles.InputField(palette));

                if (focusRow == 0)
                {
                    int textWidth = Math.Max(0, layout.InputWidth - 1);
                    int cursorX = SingleLineTextInput.GetCursorX(layout.InputX, textWidth, pattern);
                    _screen.SetCursorPosition(cursorX, layout.InputY);
                    _screen.SetCursorVisible(true);
                }
                else
                {
                    _screen.SetCursorVisible(false);
                }
            });
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width ? text.PadRight(width) : text[..width];
    }

    private readonly record struct SearchOptionsDialogLayout(
        Rect Bounds,
        int LabelWidth,
        int InputX,
        int InputY,
        int InputWidth,
        int ErrorY,
        int ButtonY)
    {
        public static SearchOptionsDialogLayout Create(ConsoleSize size, int preferredWidth, int optionCount)
        {
            int width = Math.Min(Math.Max(MinimumWidth, preferredWidth), Math.Max(MinimumWidth, size.Width));
            int height = Math.Min(Math.Max(MinimumHeight, optionCount + 8), Math.Max(MinimumHeight, size.Height));
            int x = Math.Max(0, (size.Width - width) / 2);
            int y = Math.Max(0, (size.Height - height) / 2);
            var bounds = new Rect(x, y, width, height);
            int contentX = x + 2;
            int contentY = y + 2;
            const int labelWidth = 10;
            int inputX = contentX + labelWidth;
            int inputWidth = Math.Max(1, width - 4 - labelWidth);
            return new SearchOptionsDialogLayout(
                bounds,
                labelWidth,
                inputX,
                contentY,
                inputWidth,
                contentY + optionCount + 2,
                contentY + optionCount + 3);
        }
    }
}
