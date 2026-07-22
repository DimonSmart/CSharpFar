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

    public bool IsNeutralSelected => _state.List.SelectedIndex == NeutralIndex;

    public void Refresh(CommandLineState commandLine, bool isPanelsMode, bool hasRows)
    {
        _state.ClearMatches();

        if (!isPanelsMode ||
            _state.TemporarilyHidden ||
            !hasRows ||
            string.IsNullOrWhiteSpace(commandLine.Text))
        {
            return;
        }

        var items = new List<string> { string.Empty };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var history = _history.GetCommandHistory();
        for (int i = history.Count - 1; i >= 0; i--)
        {
            string command = history[i].Command;
            if (!command.StartsWith(commandLine.Text, StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(command))
                items.Add(command);
        }

        if (items.Count == 1)
            return;

        _state.List.ResetItems(items, selectedIndex: NeutralIndex);
        _state.Visible = true;
    }

    public bool TryRemoveSelectedCommand(CommandLineState commandLine)
    {
        if (!_state.Visible ||
            _state.List.SelectedIndex == NeutralIndex ||
            _state.List.SelectedIndex >= _state.Matches.Count ||
            commandLine.HasSelection ||
            commandLine.CursorPosition != commandLine.Text.Length)
        {
            return false;
        }

        string command = _state.Matches[_state.List.SelectedIndex];
        if (string.IsNullOrEmpty(command) || !_history.RemoveCommand(command))
            return false;

        var items = _state.Matches.Where((_, index) => index != _state.List.SelectedIndex).ToArray();
        if (items.Length <= 1)
        {
            _state.ClearMatches();
            return true;
        }

        _state.List.ResetItems(items, Math.Min(_state.List.SelectedIndex, items.Length - 1));
        return true;
    }

    public void Hide(bool temporarily) =>
        _state.Reset(temporarily);
}
