
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

    private readonly List<string> _matches = [];
    private int _matchSetVersion;
    internal VerticalScrollbarController Scrollbar { get; } = new();

    public SingleLineTextHistoryState(TextHistory history) =>
        History = history ?? throw new ArgumentNullException(nameof(history));

    public TextHistory History { get; }
    public IReadOnlyList<string> Matches => _matches;
    public bool IsDropdownOpen { get; private set; }
    public int SelectedIndex { get; private set; }
    public int FirstVisibleIndex { get; private set; }
    internal int MatchSetVersion => _matchSetVersion;

    internal void ApplyCommittedSnapshot(SingleLineTextHistorySnapshot snapshot, int matchSetVersion)
    {
        if (!IsDropdownOpen || matchSetVersion != _matchSetVersion || snapshot.ItemCount != _matches.Count)
            throw new InvalidOperationException("The history frame does not belong to the current history items.");

        SelectedIndex = snapshot.SelectedIndex;
        FirstVisibleIndex = snapshot.FirstVisibleIndex;
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
        _matchSetVersion++;
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
        _matchSetVersion++;
        if (availableContentRows <= 0)
        {
            Close();
            return false;
        }

        foreach (string item in History.Items)
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

}
