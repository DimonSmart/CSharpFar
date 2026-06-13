using CSharpFar.Console;

namespace CSharpFar.Ui;

public sealed class ModuleMenuDialog
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public ModuleMenuDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public int? Show(string title, IReadOnlyList<string> items, int selected)
    {
        if (items.Count == 0)
            return null;

        var dialog = new SelectionListDialog<string>(items, static item => item, title)
        {
            SelectedIndex = Math.Clamp(selected, 0, items.Count - 1),
            MaxVisibleRows = 10,
        };
        var result = dialog.Show(_screen, _palette);
        return result.IsConfirmed ? result.SelectedIndex : null;
    }
}
