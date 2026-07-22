using CSharpFar.Ui;

namespace CSharpFar.App.CommandLine;

internal sealed class CommandCompletionState
{
    public ScrollableList<string> List { get; } = new([], static value => value);

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
}
