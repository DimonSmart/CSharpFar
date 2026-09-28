using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal sealed class EditorFindDialog
{
    private readonly DialogService _dialogs;

    public EditorFindDialog(DialogService dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public EditorSearchOptions? Show(EditorSearchOptions? previous)
    {
        var result = _dialogs.SearchOptions(new SearchOptionsDialogOptions
        {
            Title = "Find",
            InitialPattern = previous?.Pattern ?? string.Empty,
            History = AppTextHistoryIds.EditorFindPattern,
            Width = 56,
            Options = EditorSearchDialogSupport.CreateOptionLines(previous),
            Validate = state => EditorSearchDialogSupport.Validate(
                EditorSearchDialogSupport.CreateOptions(state.Pattern, state.GetOption)),
        });

        return result is null
            ? null
            : EditorSearchDialogSupport.CreateOptions(result.Pattern, result.GetOption);
    }
}
