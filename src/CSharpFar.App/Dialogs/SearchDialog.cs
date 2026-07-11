using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class SearchDialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 19;

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public SearchDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    public SearchRequest? Show(string rootPath)
    {
        return RunLoop(rootPath);
    }

    internal static SearchRequest? TryCreateRequest(
        string rootPath,
        string fileMaskExpression,
        string containingText,
        bool caseSensitive,
        bool wholeWords,
        bool notContaining,
        bool includeDirectoriesInResults,
        bool searchInSymbolicLinks,
        SearchScope scope,
        string maxDegreeOfParallelismText,
        out string? error)
    {
        string mask = string.IsNullOrWhiteSpace(fileMaskExpression)
            ? "*"
            : fileMaskExpression.Trim();

        if (!int.TryParse(maxDegreeOfParallelismText.Trim(), out int maxDegreeOfParallelism) ||
            maxDegreeOfParallelism is < 1 or > 16)
        {
            error = "Parallelism must be a number from 1 to 16.";
            return null;
        }

        string? text = containingText.Length == 0 ? null : containingText;
        error = null;
        return new SearchRequest
        {
            RootPath = rootPath,
            FileMaskExpression = mask,
            ContainingText = text,
            CaseSensitive = caseSensitive,
            WholeWords = wholeWords,
            NotContaining = text is not null && notContaining,
            IncludeDirectoriesInResults = includeDirectoriesInResults,
            SearchInSymbolicLinks = searchInSymbolicLinks,
            Scope = scope,
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
        };
    }

    internal static int DefaultParallelism() =>
        Math.Clamp(Math.Min(Environment.ProcessorCount, 4), 1, 16);

    private SearchRequest? RunLoop(string rootPath)
    {
        var mask = new CommandLineState();
        mask.SetText("*.*");
        mask.SelectAll();
        var text = new CommandLineState();
        var parallelism = new CommandLineState();
        parallelism.SetText(DefaultParallelism().ToString(System.Globalization.CultureInfo.InvariantCulture));

        SingleLineTextHistoryState maskHistory = HistoryRegistry.GetOrCreate("SearchDialog.Mask");
        SingleLineTextHistoryState textHistory = HistoryRegistry.GetOrCreate("SearchDialog.Text");
        SingleLineTextHistoryState parallelismHistory = HistoryRegistry.GetOrCreate("SearchDialog.Parallelism");
        var maskRowState = new TextInputRowState();
        var textRowState = new TextInputRowState();
        var parallelismRowState = new TextInputRowState();

        var caseSensitiveRow = new CheckBoxRow(new CheckBoxLine("Case sensitive"));
        var wholeWordsRow = new CheckBoxRow(new CheckBoxLine("Whole words"));
        var notContainingRow = new CheckBoxRow(new CheckBoxLine("Not containing"));
        var includeDirectoriesRow = new CheckBoxRow(new CheckBoxLine("Search folders"));
        var searchLinksRow = new CheckBoxRow(new CheckBoxLine("Search in symbolic links"));
        var scopeRow = new ChoiceFormRow<SearchScope>(
            new ChoiceRow<SearchScope>(
                [SearchScope.CurrentDirectoryRecursive, SearchScope.CurrentDirectoryOnly],
                ScopeLabel),
            "Select search area:");
        var buttons = new ButtonRow(
            [
                new DialogButton("find", "Find", 'F', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C'),
            ],
            FarDialogStyles.Fill,
            FarDialogStyles.FocusedInput);
        var form = new ScrollableFormDialog();
        string? error = null;

        using var modal = _modalDialogs.Open(context => Draw(context, form, error));
        while (true)
        {
            bool hasText = text.Text.Length > 0;
            form.SetRows(BuildRows(
                mask,
                text,
                parallelism,
                maskHistory,
                textHistory,
                parallelismHistory,
                maskRowState,
                textRowState,
                parallelismRowState,
                caseSensitiveRow,
                wholeWordsRow,
                notContainingRow,
                includeDirectoriesRow,
                searchLinksRow,
                scopeRow,
                buttons,
                hasText));
            modal.Render();

            var input = modal.ReadInput();
            FormInputResult result = input switch
            {
                KeyConsoleInputEvent { Key: var key } => HandleSearchKey(form, key),
                MouseConsoleInputEvent mouse => form.HandleMouse(mouse),
                _ => FormInputResult.NotHandled,
            };

            if (result.Kind == FormInputResultKind.Cancel)
                return null;

            if (result.Kind == FormInputResultKind.Submit ||
                input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 })
            {
                var request = BuildRequest(
                    rootPath,
                    mask,
                    text,
                    caseSensitiveRow.Value,
                    wholeWordsRow.Value,
                    notContainingRow.Value,
                    includeDirectoriesRow.Value,
                    searchLinksRow.Value,
                    scopeRow.Value,
                    parallelism,
                    maskHistory,
                    textHistory,
                    parallelismHistory,
                    ref error);
                if (request is not null)
                    return request;
            }
        }
    }

    private static IReadOnlyList<IFormRow> BuildRows(
        CommandLineState mask,
        CommandLineState text,
        CommandLineState parallelism,
        SingleLineTextHistoryState maskHistory,
        SingleLineTextHistoryState textHistory,
        SingleLineTextHistoryState parallelismHistory,
        TextInputRowState maskRowState,
        TextInputRowState textRowState,
        TextInputRowState parallelismRowState,
        CheckBoxRow caseSensitive,
        CheckBoxRow wholeWords,
        CheckBoxRow notContaining,
        CheckBoxRow includeDirectories,
        CheckBoxRow searchLinks,
        ChoiceFormRow<SearchScope> scope,
        ButtonRow buttons,
        bool hasText)
    {
        var fill = FarDialogStyles.Fill;
        var disabled = new CellStyle(ConsoleColor.DarkGray, fill.Background);
        return
        [
            new LabelRow("A file mask or several file masks:", fill),
            new TextInputRow(mask, maskHistory, maskRowState) { Id = "mask", SubmitOnEnter = true },
            new LabelRow("Containing text:", fill),
            new TextInputRow(text, textHistory, textRowState) { Id = "text", SubmitOnEnter = true },
            new LabelRow("Using code page: Automatic detection", fill),
            caseSensitive,
            wholeWords,
            hasText ? notContaining : new LabelRow("[ ] Not containing", disabled),
            includeDirectories,
            searchLinks,
            scope,
            new LabelRow("Parallelism:", fill),
            new TextInputRow(parallelism, parallelismHistory, parallelismRowState, width: 8)
            {
                Id = "parallelism",
                SubmitOnEnter = true,
            },
            new SeparatorRow(fill, drawLine: false),
            buttons,
        ];
    }

    private static FormInputResult HandleSearchKey(ScrollableFormDialog form, ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.F10)
            return FormInputResult.Submit("find");
        if (key.Key == ConsoleKey.Enter && form.IsFocusedOnSubmitRow)
            return FormInputResult.Submit("find");

        return form.HandleKey(key);
    }

    private SearchRequest? BuildRequest(
        string rootPath,
        CommandLineState mask,
        CommandLineState text,
        bool caseSensitive,
        bool wholeWords,
        bool notContaining,
        bool includeDirectoriesInResults,
        bool searchInSymbolicLinks,
        SearchScope scope,
        CommandLineState parallelism,
        SingleLineTextHistoryState maskHistory,
        SingleLineTextHistoryState textHistory,
        SingleLineTextHistoryState parallelismHistory,
        ref string? error)
    {
        var request = TryCreateRequest(
            rootPath,
            mask.Text,
            text.Text,
            caseSensitive,
            wholeWords,
            notContaining,
            includeDirectoriesInResults,
            searchInSymbolicLinks,
            scope,
            parallelism.Text,
            out error);

        if (request is null)
            return null;

        maskHistory.Add(request.FileMaskExpression);
        if (request.ContainingText is not null)
            textHistory.Add(request.ContainingText);
        parallelismHistory.Add(request.MaxDegreeOfParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture));
        maskHistory.Close();
        textHistory.Close();
        parallelismHistory.Close();
        return request;
    }

    private void Draw(UiRenderContext context, ScrollableFormDialog form, string? error)
    {
        Rect outerBounds = OuterBounds(context.Size);

        _modalRenderer.Render(context.Screen, outerBounds, "Find file", true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);
            int errorY = bounds.Y + bounds.Height - 2;
            int bodyTop = bounds.Y + 1;
            int bodyHeight = Math.Max(1, errorY - bodyTop);

            form.Render(new FormRenderContext(
                context,
                new Rect(contentX, bodyTop, contentWidth, bodyHeight),
                FarDialogStyles.Border));

            string errorText = error is null ? string.Empty : Truncate(error, contentWidth);
            context.Screen.Write(contentX, errorY, errorText.PadRight(contentWidth), FarDialogStyles.Error);
        });
    }

    private static Rect OuterBounds(ConsoleSize size)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(48, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        return new Rect(dialogX, dialogY, dialogWidth, dialogHeight);
    }

    private static string ScopeLabel(SearchScope scope) => scope switch
    {
        SearchScope.CurrentDirectoryOnly => "In current folder",
        _ => "From the current folder",
    };

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "~";
    }
}
