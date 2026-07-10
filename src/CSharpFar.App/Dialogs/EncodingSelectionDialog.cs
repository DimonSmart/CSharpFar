using CSharpFar.Console;
using CSharpFar.Core.Text;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class EncodingSelectionDialog
{
    private readonly ScreenRenderer _screen;

    public EncodingSelectionDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public TextEncodingCatalogItem? Show(
        IReadOnlyList<TextEncodingCatalogItem> items,
        TextEncodingSelection currentSelection,
        Action<TextEncodingCatalogItem>? previewSelection = null,
        Action? previewRedraw = null)
    {
        if (items.Count == 0)
            return null;

        var dialog = new SelectionListDialog<TextEncodingCatalogItem>(items, static item => item.Label, "Encoding")
        {
            DoubleBorder = true,
            MaxWidth = 44,
            MaxVisibleRows = items.Count,
            SelectedIndex = FindInitialCursor(items, currentSelection),
            SelectionChanged = (item, _) =>
            {
                previewSelection?.Invoke(item);
                previewRedraw?.Invoke();
            },
        };

        var result = dialog.Show(_screen);
        return result.IsConfirmed ? result.SelectedItem : null;
    }

    private static int FindInitialCursor(
        IReadOnlyList<TextEncodingCatalogItem> items,
        TextEncodingSelection currentSelection)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Selection == currentSelection)
                return i;
        }

        return 0;
    }
}
