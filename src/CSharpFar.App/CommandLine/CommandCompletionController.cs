using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.App.CommandLine;

internal sealed class CommandCompletionController
{
    private const int NeutralIndex = 0;

    private readonly IHistoryStore _history;
    private readonly CommandCompletionState _state;

    public CommandCompletionController(IHistoryStore history, CommandCompletionState state)
    {
        _history = history;
        _state = state;
    }

    public bool IsNeutralSelected => _state.SelectedIndex == NeutralIndex;

    public void Refresh(CommandLineState commandLine, bool hasVisiblePanels, bool hasRows)
    {
        _state.ClearMatches();

        if (!hasVisiblePanels ||
            _state.TemporarilyHidden ||
            !hasRows ||
            string.IsNullOrWhiteSpace(commandLine.Text))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var history = _history.GetCommandHistory();
        for (int i = history.Count - 1; i >= 0; i--)
        {
            string command = history[i].Command;
            if (!command.StartsWith(commandLine.Text, StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(command))
                _state.Matches.Add(command);
        }

        if (_state.Matches.Count == 0)
            return;

        _state.Matches.Insert(NeutralIndex, string.Empty);
        _state.Visible = true;
        _state.FirstVisibleIndex = 0;
    }

    public void ClampSelectionToViewport(int visibleRows)
    {
        if (_state.Matches.Count == 0)
        {
            _state.SelectedIndex = 0;
            _state.FirstVisibleIndex = 0;
            return;
        }

        int lastVisibleIndex = Math.Min(
            _state.Matches.Count - 1,
            _state.FirstVisibleIndex + visibleRows - 1);
        _state.SelectedIndex = Math.Clamp(
            _state.SelectedIndex,
            _state.FirstVisibleIndex,
            lastVisibleIndex);
    }

    public bool TryMoveSelection(int delta, int visibleRows)
    {
        if (!_state.Visible || _state.Matches.Count == 0 || visibleRows <= 0)
            return false;

        _state.SelectedIndex = Math.Clamp(
            _state.SelectedIndex + delta,
            0,
            _state.Matches.Count - 1);
        _state.FirstVisibleIndex = ScrollStateCalculator.EnsureIndexVisible(
            _state.SelectedIndex,
            _state.FirstVisibleIndex,
            visibleRows);
        _state.FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            _state.FirstVisibleIndex,
            _state.Matches.Count,
            visibleRows);
        return true;
    }

    public void Hide(bool temporarily) =>
        _state.Reset(temporarily);
}
