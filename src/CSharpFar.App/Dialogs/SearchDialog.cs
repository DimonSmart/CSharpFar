using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class SearchDialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 18;

    private readonly FormFieldFactory _fields;
    private readonly DialogService _dialogs;

    public SearchDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
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
        TextField mask = fields.Text(new TextFieldOptions("*.*", AppTextHistoryIds.SearchMask, SubmitOnEnter: true));
        mask.SelectAll();
        TextField text = fields.Text(new TextFieldOptions(HistoryId: AppTextHistoryIds.SearchText, SubmitOnEnter: true));
        TextField parallelism = fields.Text(new TextFieldOptions(
            DefaultParallelism().ToString(System.Globalization.CultureInfo.InvariantCulture),
            AppTextHistoryIds.SearchParallelism,
            Width: 8,
            SubmitOnEnter: true));

        var caseSensitiveRow = FormControls.CheckBox("Case sensitive");
        var wholeWordsRow = FormControls.CheckBox("Whole words");
        var notContainingRow = FormControls.CheckBox("Not containing");
        var includeDirectoriesRow = FormControls.CheckBox("Include folders in results");
        var searchLinksRow = FormControls.CheckBox("Search in symbolic links");
        SearchScope[] scopes =
        [
            SearchScope.CurrentDirectoryRecursive,
            SearchScope.CurrentDirectoryOnly,
        ];
        var scopeRow = FormControls.Dropdown(
            string.Empty, scopes, ScopeLabel, SearchScope.CurrentDirectoryRecursive);
        scopeRow.MaxVisibleRows = 6;
        var optionsRow = FormControls.CheckBoxColumns(
            [
                [caseSensitiveRow, wholeWordsRow, notContainingRow],
                [includeDirectoriesRow, searchLinksRow],
            ]);
        var buttons = FormControls.Buttons(
            DialogButton.Default("find", "Find", 'F'),
            DialogButton.Cancel());
        return _dialogs.Form(
            new FormDialogOptions("Find file", DialogWidth, DialogHeight, MinWidth: 48),
            rows: () => BuildBodyRows(mask, text, parallelism, notContainingRow, optionsRow, scopeRow, text.Text.Length > 0),
            footer: () => [buttons],
            submit: () =>
            {
                SearchRequest? request = TryCreateRequest(
                    rootPath, mask.Text, text.Text, caseSensitiveRow.Value, wholeWordsRow.Value,
                    notContainingRow.Value, includeDirectoriesRow.Value, searchLinksRow.Value,
                    scopeRow.Value, parallelism.Text, out string? error);
                if (request is null)
                    return FormSubmit.Invalid<SearchRequest?>(error!, parallelism);

                mask.Text = request.FileMaskExpression;
                if (request.ContainingText is not null)
                    text.Text = request.ContainingText;
                parallelism.Text = request.MaxDegreeOfParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return FormSubmit.Success<SearchRequest?>(request);
            });
    }

    private static IReadOnlyList<FormRow> BuildBodyRows(
        TextField mask,
        TextField text,
        TextField parallelism,
        CheckBoxRow notContaining,
        CheckBoxColumnsRow options,
        DropdownSelectFormRow<SearchScope> scope,
        bool hasText)
    {
        notContaining.Enabled = hasText;
        return
        [
            FormControls.Label("A file mask or several file masks:"),
            FormControls.Text(mask),
            FormControls.Label("Containing text:"),
            FormControls.Text(text),
            FormControls.Label("Using code page: Automatic detection"),
            options,
            FormControls.Label("Select search area:"),
            scope,
            FormControls.Label("Parallelism:"),
            FormControls.Text(parallelism),
        ];
    }

    private static string ScopeLabel(SearchScope scope) => scope switch
    {
        SearchScope.CurrentDirectoryRecursive => "Current folder and subfolders",
        SearchScope.CurrentDirectoryOnly => "Current folder only",
        _ => scope.ToString(),
    };

}
