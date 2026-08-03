using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class SearchDialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 18;

    private readonly FormFieldFactory _fields;
    private readonly ModalFormHost _formDialogs;

    public SearchDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
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
        var fields = _fields;
        TextField mask = fields.Text("mask", "*.*", AppTextHistoryIds.SearchMask, submitOnEnter: true);
        mask.SelectAll();
        TextField text = fields.Text("text", historyId: AppTextHistoryIds.SearchText, submitOnEnter: true);
        TextField parallelism = fields.Text("parallelism", DefaultParallelism().ToString(System.Globalization.CultureInfo.InvariantCulture),
            AppTextHistoryIds.SearchParallelism, width: 8, submitOnEnter: true);

        var caseSensitiveRow = FormControls.CheckBox("case-sensitive", "Case sensitive");
        var wholeWordsRow = FormControls.CheckBox("whole-words", "Whole words");
        var notContainingRow = FormControls.CheckBox("not-containing", "Not containing");
        var includeDirectoriesRow = FormControls.CheckBox("include-directories", "Include folders in results");
        var searchLinksRow = FormControls.CheckBox("search-links", "Search in symbolic links");
        SearchScope[] scopes =
        [
            SearchScope.CurrentDirectoryRecursive,
            SearchScope.CurrentDirectoryOnly,
        ];
        var scopeRow = FormControls.Dropdown(
            "scope", string.Empty, scopes, ScopeLabel, SearchScope.CurrentDirectoryRecursive);
        scopeRow.MaxVisibleRows = 6;
        var optionsRow = FormControls.CheckBoxColumns(
            "search-options",
            [
                [caseSensitiveRow, wholeWordsRow, notContainingRow],
                [includeDirectoriesRow, searchLinksRow],
            ],
            columnGap: 2);
        var buttons = FormControls.Buttons(
            "footerButtons",
            DialogButton.Default("find", "Find", 'F'),
            DialogButton.Cancel());
        var form = new ScrollableFormDialog();
        string? error = null;

        void PrepareRows()
        {
            bool hasText = text.Text.Length > 0;
            form.SetRows(
                BuildBodyRows(
                    mask,
                    text,
                    parallelism,
                    notContainingRow,
                    optionsRow,
                    scopeRow,
                    hasText),
                FormFooter.ErrorAndButtons(() => error is null ? null : Truncate(error, DialogWidth), buttons));
        }

        return _formDialogs.Run(
            form,
            new ModalFormOptions("Find file", DialogWidth, DialogHeight, MinWidth: 48),
            static layout => ModalFormLayout.WithFooter(layout.ContentBounds, footerHeight: 2),
            (routed, result) =>
            {
                if (FormDialogInput.ShouldCancel(result))
                    return ModalDialogLoopResult<SearchRequest?>.Complete(null);

                if (FormDialogInput.ShouldSubmit(routed, result, form))
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
                        ref error);
                    if (request is not null)
                        return ModalDialogLoopResult<SearchRequest?>.Complete(request);
                }

                return ModalDialogLoopResult<SearchRequest?>.ContinueNoChange;
            },
            prepareRender: PrepareRows);
    }

    private static IReadOnlyList<IFormRow> BuildBodyRows(
        TextField mask,
        TextField text,
        TextField parallelism,
        CheckBoxRow notContaining,
        CheckBoxColumnsRow options,
        DropdownSelectFormRow<SearchScope> scope,
        bool hasText)
    {
        var fill = FarDialogStyles.Fill;
        notContaining.Enabled = hasText;
        return
        [
            new LabelRow("A file mask or several file masks:", fill),
            FormControls.Text(mask),
            new LabelRow("Containing text:", fill),
            FormControls.Text(text),
            new LabelRow("Using code page: Automatic detection", fill),
            options,
            new LabelRow("Select search area:", fill),
            scope,
            new LabelRow("Parallelism:", fill),
            FormControls.Text(parallelism),
        ];
    }

    private SearchRequest? BuildRequest(
        string rootPath,
        TextField mask,
        TextField text,
        bool caseSensitive,
        bool wholeWords,
        bool notContaining,
        bool includeDirectoriesInResults,
        bool searchInSymbolicLinks,
        SearchScope scope,
        TextField parallelism,
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

        mask.Text = request.FileMaskExpression;
        mask.AcceptHistory();
        if (request.ContainingText is not null)
        {
            text.Text = request.ContainingText;
            text.AcceptHistory();
        }
        parallelism.Text = request.MaxDegreeOfParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture);
        parallelism.AcceptHistory();
        return request;
    }

    private static string ScopeLabel(SearchScope scope) => scope switch
    {
        SearchScope.CurrentDirectoryRecursive => "Current folder and subfolders",
        SearchScope.CurrentDirectoryOnly => "Current folder only",
        _ => scope.ToString(),
    };

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "~";
    }
}
