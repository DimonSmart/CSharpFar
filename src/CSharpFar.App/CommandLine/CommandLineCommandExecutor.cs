using CSharpFar.App.Modules;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.CommandLine;

internal sealed class CommandLineCommandExecutor
{
    private readonly CommandLineState _commandLine;
    private readonly CommandHistoryNavigator _historyNavigator;
    private readonly IHistoryStore _history;
    private readonly ModulePanelOpener _modulePanelOpener;
    private readonly ChangeDirectoryCommandExecutor _changeDirectoryCommandExecutor;
    private readonly IShellService _shell;
    private readonly ExternalConsoleCommandRunner _externalCommandRunner;
    private readonly Func<FilePanelState> _activeState;
    private readonly Func<PanelSide> _activeSide;
    private readonly Action _closePanelQuickSearch;
    private readonly Action<bool> _hideCommandCompletion;

    public CommandLineCommandExecutor(
        CommandLineState commandLine,
        CommandHistoryNavigator historyNavigator,
        IHistoryStore history,
        ModulePanelOpener modulePanelOpener,
        ChangeDirectoryCommandExecutor changeDirectoryCommandExecutor,
        IShellService shell,
        ExternalConsoleCommandRunner externalCommandRunner,
        Func<FilePanelState> activeState,
        Func<PanelSide> activeSide,
        Action closePanelQuickSearch,
        Action<bool> hideCommandCompletion)
    {
        _commandLine = commandLine;
        _historyNavigator = historyNavigator;
        _history = history;
        _modulePanelOpener = modulePanelOpener;
        _changeDirectoryCommandExecutor = changeDirectoryCommandExecutor;
        _shell = shell;
        _externalCommandRunner = externalCommandRunner;
        _activeState = activeState;
        _activeSide = activeSide;
        _closePanelQuickSearch = closePanelQuickSearch;
        _hideCommandCompletion = hideCommandCompletion;
    }

    public void Execute(string command)
    {
        string workDir = _activeState().CurrentDirectory;
        _closePanelQuickSearch();
        _commandLine.Clear();
        _hideCommandCompletion(false);
        _historyNavigator.Reset();
        AddCommandHistory(command, workDir);

        if (_modulePanelOpener.TryOpenFromCommandLine(command, _activeSide()))
            return;

        if (_changeDirectoryCommandExecutor.TryExecute(command))
            return;

        _externalCommandRunner.Execute(workDir, command, () => _shell.Execute(command, workDir));
    }

    private void AddCommandHistory(string command, string workingDirectory)
    {
        _history.AddCommand(new CommandHistoryItem
        {
            Command = command,
            WorkingDirectory = workingDirectory,
        });
    }
}
