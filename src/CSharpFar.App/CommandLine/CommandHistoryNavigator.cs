using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.App.CommandLine;

internal sealed class CommandHistoryNavigator
{
    private readonly IHistoryStore _history;
    private int? _navigationIndex;

    public CommandHistoryNavigator(IHistoryStore history)
    {
        _history = history;
    }

    public bool Browse(CommandLineState commandLine, int direction, CommandHistoryNavigationStart start)
    {
        var history = _history.GetCommandHistory();
        if (history.Count == 0)
            return true;

        if (_navigationIndex is null)
        {
            _navigationIndex = start == CommandHistoryNavigationStart.Newest
                ? history.Count - 1
                : 0;
        }
        else
        {
            _navigationIndex = Math.Clamp(
                _navigationIndex.Value + direction,
                0,
                history.Count - 1);
        }

        commandLine.SetText(history[_navigationIndex.Value].Command);
        return true;
    }

    public void Reset() =>
        _navigationIndex = null;
}
