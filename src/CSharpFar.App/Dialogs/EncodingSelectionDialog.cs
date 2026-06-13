using CSharpFar.Console;
using CSharpFar.Core.Text;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class EncodingSelectionDialog
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public EncodingSelectionDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public TextEncodingCatalogItem? Show(
        IReadOnlyList<TextEncodingCatalogItem> items,
        TextEncodingSelection currentSelection,
        Action<TextEncodingCatalogItem>? previewSelection = null,
        Action? renderUnderlay = null)
    {
        if (items.Count == 0)
            return null;

        var dialog = new SelectionListDialog<TextEncodingCatalogItem>(items, static item => item.Label, "Encoding")
        {
            DoubleBorder = true,
            MaxWidth = 44,
            MaxVisibleRows = items.Count,
            SelectedIndex = FindInitialCursor(items, currentSelection),
            RenderUnderlay = renderUnderlay,
            SelectionChanged = (item, _) =>
            {
                previewSelection?.Invoke(item);
            },
        };

        var result = dialog.Show(_screen, _palette);
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
