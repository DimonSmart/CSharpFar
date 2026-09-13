namespace CSharpFar.App.CommandLine;

internal sealed class CommandCompletionState
{
    private IReadOnlyList<string> _items = [];
    private int _selectedIndex = -1;

    public IReadOnlyList<string> Items => _items;
    public IReadOnlyList<string> List => _items;
    public IReadOnlyList<string> Matches => _items;
    public int Count => _items.Count;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => _selectedIndex = _items.Count == 0
            ? -1
            : Math.Clamp(value, 0, _items.Count - 1);
    }

    public bool Visible { get; set; }
    public bool TemporarilyHidden { get; set; }

    public void SetItems(IEnumerable<string> items, int selectedIndex = 0)
    {
        _items = Array.AsReadOnly(items.ToArray());
        SelectedIndex = selectedIndex;
    }

    public void ClearMatches()
    {
        Visible = false;
        _items = [];
        _selectedIndex = -1;
    }

    public void Reset(bool temporarilyHidden)
    {
        ClearMatches();
        TemporarilyHidden = temporarilyHidden;
    }

    public void CloseForHiddenScroll() => Reset(temporarilyHidden: false);
}
