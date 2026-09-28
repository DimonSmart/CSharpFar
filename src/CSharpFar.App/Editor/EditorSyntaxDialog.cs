using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal sealed class EditorSyntaxDialog
{
    private const string AutoText = "Auto";
    private const string CustomScopeText = "Custom TextMate scope...";

    private readonly DialogService _dialogs;
    private readonly IEditorSyntaxCatalog _catalog;

    public EditorSyntaxDialog(
        DialogService dialogs,
        IEditorSyntaxCatalog catalog)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public string? ShowLanguage(string currentLanguage)
    {
        string current = currentLanguage?.Trim() ?? string.Empty;
        var choices = BuildLanguageChoices();
        int selectedIndex = InitialLanguageIndex(choices, current);
        int customIndex = choices.Count - 1;
        string customInitialText = selectedIndex == customIndex ? current : string.Empty;

        while (true)
        {
            var result = _dialogs.Select(new SelectionDialogOptions<LanguageChoice>
            {
                Title = "Syntax language",
                Items = choices,
                ItemText = static choice => choice.DisplayText,
                SearchText = static choice => choice.SearchText,
                SelectedIndex = selectedIndex,
                EnableFilter = true,
            });
            if (!result.IsConfirmed || result.SelectedItem is null)
                return null;

            selectedIndex = result.SelectedIndex;
            var choice = result.SelectedItem;
            switch (choice.Kind)
            {
                case LanguageChoiceKind.Auto:
                    return "auto";
                case LanguageChoiceKind.Known:
                    return choice.CatalogEntry!.Language.Id;
                case LanguageChoiceKind.Custom:
                    string? customScope = _dialogs.Input(new SingleLineInputDialogOptions
                    {
                        Title = "Custom TextMate scope",
                        Prompt = "Scope",
                        AllowEmpty = false,
                        InitialText = customInitialText,
                        Validate = ValidateCustomScope,
                    });
                    if (customScope is not null)
                        return customScope.Trim();

                    selectedIndex = customIndex;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public string? ShowTheme(string currentTheme)
    {
        var themes = _catalog.Themes
            .OrderBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(theme => theme.Name, StringComparer.Ordinal)
            .ToArray();
        if (themes.Length == 0)
            return null;

        EditorSyntaxTheme resolvedTheme = _catalog.ResolveTheme(currentTheme);
        int selectedIndex = Array.FindIndex(
            themes,
            theme => string.Equals(theme.Name, resolvedTheme.Name, StringComparison.Ordinal));
        if (selectedIndex < 0)
            selectedIndex = 0;

        var result = _dialogs.Select(new SelectionDialogOptions<EditorSyntaxTheme>
        {
            Title = "Syntax theme",
            Items = themes,
            ItemText = static theme => theme.Name,
            SelectedIndex = selectedIndex,
        });

        return result.IsConfirmed
            ? result.SelectedItem?.Name
            : null;
    }

    internal static string? ValidateCustomScope(string value)
    {
        string scope = value?.Trim() ?? string.Empty;
        return scope.StartsWith("source.", StringComparison.Ordinal) ||
               scope.StartsWith("text.", StringComparison.Ordinal)
            ? null
            : "Scope must start with 'source.' or 'text.'.";
    }

    private IReadOnlyList<LanguageChoice> BuildLanguageChoices()
    {
        var choices = new List<LanguageChoice>
        {
            new(LanguageChoiceKind.Auto, AutoText, AutoText, null),
        };

        foreach (var entry in _catalog.Languages
                     .OrderBy(item => item.Language.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Language.Id, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Language.Id, StringComparer.Ordinal))
        {
            choices.Add(new LanguageChoice(
                LanguageChoiceKind.Known,
                entry.Language.DisplayName,
                entry.SearchText,
                entry));
        }

        choices.Add(new LanguageChoice(
            LanguageChoiceKind.Custom,
            CustomScopeText,
            CustomScopeText,
            null));
        return choices;
    }

    private int InitialLanguageIndex(
        IReadOnlyList<LanguageChoice> choices,
        string currentLanguage)
    {
        if (string.Equals(currentLanguage, "auto", StringComparison.OrdinalIgnoreCase))
            return 0;

        EditorSyntaxLanguage? resolved = _catalog.ResolveLanguage(currentLanguage);
        if (resolved is not null)
        {
            for (int index = 1; index < choices.Count - 1; index++)
            {
                var known = choices[index].CatalogEntry?.Language;
                if (known is not null &&
                    string.Equals(known.ScopeName, resolved.ScopeName, StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        return IsCustomScope(currentLanguage)
            ? choices.Count - 1
            : 0;
    }

    private static bool IsCustomScope(string value) =>
        value.StartsWith("source.", StringComparison.Ordinal) ||
        value.StartsWith("text.", StringComparison.Ordinal);

    private enum LanguageChoiceKind
    {
        Auto,
        Known,
        Custom,
    }

    private sealed record LanguageChoice(
        LanguageChoiceKind Kind,
        string DisplayText,
        string SearchText,
        EditorSyntaxLanguageCatalogEntry? CatalogEntry);
}
