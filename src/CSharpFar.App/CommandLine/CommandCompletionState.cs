using CSharpFar.Ui;

namespace CSharpFar.App.CommandLine;

internal sealed class CommandCompletionState
{
    public ScrollableListState<string> List { get; } = new([]);

    public IReadOnlyList<string> Matches => List.Items;

    public bool Visible { get; set; }

    public bool TemporarilyHidden { get; set; }

    public void ClearMatches()
    {
        Visible = false;
        List.ResetItems([]);
    }

    public void Reset(bool temporarilyHidden)
    {
        ClearMatches();
        TemporarilyHidden = temporarilyHidden;
    }

    public void CloseForHiddenScroll() => Reset(temporarilyHidden: false);
}
