using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public enum SingleLineTextHistoryAcceptResult
{
    NotAccepted,
    CurrentText,
    HistoryItem,
}

public sealed class SingleLineTextHistoryState
{
    public const int MaxVisibleRows = 10;
    public const int MaxItemsPerField = 100;

    private readonly List<string> _items = [];
    private readonly List<string> _matches = [];
    internal VerticalScrollbarController Scrollbar { get; } = new();

    public SingleLineTextHistoryState(IEnumerable<string>? initialItems = null, Action<IReadOnlyList<string>>? itemsChanged = null)
    {
        _items = Normalize(initialItems);
        ItemsChanged = itemsChanged;
    }

    private Action<IReadOnlyList<string>>? ItemsChanged { get; }
    public IReadOnlyList<string> Items => _items;
    public bool HasItems => _items.Count > 0;
    public IReadOnlyList<string> Matches => _matches;
    public bool IsDropdownOpen { get; private set; }
    public int SelectedIndex { get; private set; }
    public int FirstVisibleIndex { get; private set; }

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (_items.Count > 0 && string.Equals(_items[0], text, StringComparison.Ordinal))
            return;

        _items.RemoveAll(item => string.Equals(item, text, StringComparison.Ordinal));
        _items.Insert(0, text);
        if (_items.Count > MaxItemsPerField)
            _items.RemoveRange(MaxItemsPerField, _items.Count - MaxItemsPerField);
        RefreshOpenMatches();
        ItemsChanged?.Invoke(_items);
    }

    public bool OpenAll(int availableContentRows) =>
        OpenMatches(prefix: string.Empty, availableContentRows);

    public bool OpenForPrefix(string prefix, int availableContentRows)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            Close();
            return false;
        }

        return OpenMatches(prefix, availableContentRows);
    }

    public void Close()
    {
        IsDropdownOpen = false;
        _matches.Clear();
        SelectedIndex = 0;
        FirstVisibleIndex = 0;
        Scrollbar.ApplyCommittedFrame(null);
    }

    public bool MoveSelection(int delta, int availableContentRows)
    {
        if (!IsDropdownOpen || _matches.Count == 0)
            return false;

        int visibleRows = VisibleRows(availableContentRows, _matches.Count);
        if (visibleRows <= 0)
        {
            Close();
            return false;
        }

        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, _matches.Count - 1);
        FirstVisibleIndex = ScrollStateCalculator.EnsureIndexVisible(
            SelectedIndex,
            FirstVisibleIndex,
            visibleRows);
        FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            FirstVisibleIndex,
            _matches.Count,
            visibleRows);
        return true;
    }

    public bool Select(int itemIndex, int availableContentRows)
    {
        if (!IsDropdownOpen || _matches.Count == 0)
            return false;

        int visibleRows = VisibleRows(availableContentRows, _matches.Count);
        if (visibleRows <= 0)
        {
            Close();
            return false;
        }

        SelectedIndex = Math.Clamp(itemIndex, 0, _matches.Count - 1);
        FirstVisibleIndex = ScrollStateCalculator.EnsureIndexVisible(
            SelectedIndex,
            FirstVisibleIndex,
            visibleRows);
        FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            FirstVisibleIndex,
            _matches.Count,
            visibleRows);
        return true;
    }

    public void SetFirstVisibleIndex(int firstVisibleIndex, int availableContentRows)
    {
        int visibleRows = VisibleRows(availableContentRows, _matches.Count);
        FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            firstVisibleIndex,
            _matches.Count,
            visibleRows);
        if (_matches.Count > 0)
            SelectedIndex = Math.Clamp(SelectedIndex, FirstVisibleIndex, FirstVisibleIndex + Math.Max(0, visibleRows - 1));
    }

    public SingleLineTextHistoryAcceptResult AcceptSelected(CommandLineState buffer)
    {
        if (!IsDropdownOpen || _matches.Count == 0)
            return SingleLineTextHistoryAcceptResult.NotAccepted;

        int selectedIndex = Math.Clamp(SelectedIndex, 0, _matches.Count - 1);
        if (selectedIndex == 0)
        {
            Close();
            return SingleLineTextHistoryAcceptResult.CurrentText;
        }

        string selectedItem = _matches[selectedIndex];
        Close();
        buffer.SetText(selectedItem);
        return SingleLineTextHistoryAcceptResult.HistoryItem;
    }

    public int VisibleRows(int availableContentRows) =>
        VisibleRows(availableContentRows, _matches.Count);

    private bool OpenMatches(string prefix, int availableContentRows)
    {
        _matches.Clear();
        if (availableContentRows <= 0)
        {
            Close();
            return false;
        }

        foreach (string item in _items)
        {
            if ((prefix.Length == 0 || item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                _matches.Count < MaxVisibleRows - 1)
                _matches.Add(item);
        }

        if (_matches.Count == 0)
        {
            Close();
            return false;
        }

        _matches.Insert(0, string.Empty);
        IsDropdownOpen = true;
        SelectedIndex = 0;
        FirstVisibleIndex = 0;
        NormalizeSelection(availableContentRows);
        return true;
    }

    private void RefreshOpenMatches()
    {
        if (!IsDropdownOpen)
            return;

        for (int index = _matches.Count - 1; index > 0; index--)
        {
            if (!_items.Contains(_matches[index], StringComparer.Ordinal))
                _matches.RemoveAt(index);
        }
        if (_matches.Count == 1)
            Close();
    }

    private void NormalizeSelection(int availableContentRows)
    {
        int visibleRows = VisibleRows(availableContentRows, _matches.Count);
        if (visibleRows <= 0)
        {
            Close();
            return;
        }

        int selectedIndex = SelectedIndex;
        int firstVisibleIndex = FirstVisibleIndex;
        ScrollStateCalculator.NormalizeSelection(
            _matches.Count,
            visibleRows,
            ref selectedIndex,
            ref firstVisibleIndex);
        SelectedIndex = selectedIndex;
        FirstVisibleIndex = firstVisibleIndex;
    }

    private static int VisibleRows(int availableContentRows, int itemCount) =>
        Math.Max(0, Math.Min(Math.Min(MaxVisibleRows, availableContentRows), itemCount));

    private static List<string> Normalize(IEnumerable<string>? items)
    {
        var result = new List<string>();
        if (items is null)
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string item in items)
        {
            if (!string.IsNullOrWhiteSpace(item) && seen.Add(item))
                result.Add(item);
            if (result.Count == MaxItemsPerField)
                break;
        }
        return result;
    }
}
