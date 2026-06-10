using CSharpFar.Ui;

namespace CSharpFar.App.CommandLine;

internal sealed class CommandCompletionState
{
    public List<string> Matches { get; } = [];

    public bool Visible { get; set; }

    public bool TemporarilyHidden { get; set; }

    public int SelectedIndex { get; set; }

    public int FirstVisibleIndex { get; set; }

    public ScrollBarDragState? ScrollbarDrag { get; set; }

    public void ClearMatches()
    {
        Visible = false;
        Matches.Clear();
        SelectedIndex = 0;
        FirstVisibleIndex = 0;
        ScrollbarDrag = null;
    }

    public void Reset(bool temporarilyHidden)
    {
        ClearMatches();
        TemporarilyHidden = temporarilyHidden;
    }
}
