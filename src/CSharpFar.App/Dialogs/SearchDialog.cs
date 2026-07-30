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

    private readonly SingleLineTextHistoryRegistry _historyRegistry;
    private readonly ModalFormHost _formDialogs;

    public SearchDialog(ModalDialogHost modalDialogs, SingleLineTextHistoryRegistry? historyRegistry = null)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _historyRegistry = historyRegistry ?? new SingleLineTextHistoryRegistry();
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

        SingleLineTextHistoryState maskHistory = _historyRegistry.GetOrCreate("SearchDialog.Mask");
        SingleLineTextHistoryState textHistory = _historyRegistry.GetOrCreate("SearchDialog.Text");
        SingleLineTextHistoryState parallelismHistory = _historyRegistry.GetOrCreate("SearchDialog.Parallelism");
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
                new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
            ]);
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
                    hasText),
                [
                    new LabelRow(error is null ? string.Empty : Truncate(error, DialogWidth), FarDialogStyles.Error),
                    buttons,
                ]);
        }

        return _formDialogs.Run(
            form,
            new ModalFormOptions("Find file", DialogWidth, DialogHeight, MinWidth: 48),
            static layout =>
            {
                Rect content = layout.ContentBounds;
                return new ModalFormLayout(
                    new Rect(content.X, content.Y, content.Width, Math.Max(1, content.Height - 2)),
                    new Rect(content.X, content.Bottom - 2, content.Width, 2));
            },
            (routed, result) =>
            {
                if (result.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<SearchRequest?>.Complete(null);

                if (result.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
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
                        return ModalDialogLoopResult<SearchRequest?>.Complete(request);
                }

                return ModalDialogLoopResult<SearchRequest?>.ContinueNoChange;
            },
            prepareRender: PrepareRows);
    }

    private static IReadOnlyList<IFormRow> BuildBodyRows(
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
        bool hasText)
    {
        var fill = FarDialogStyles.Fill;
        var disabled = FarDialogStyles.DisabledControl(fill);
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
        ];
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
