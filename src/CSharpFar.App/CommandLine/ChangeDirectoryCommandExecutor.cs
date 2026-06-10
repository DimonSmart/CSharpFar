using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.CommandLine;

internal sealed class ChangeDirectoryCommandExecutor
{
    private readonly PanelController _controller;
    private readonly Func<FilePanelState> _activeState;
    private readonly Func<PanelSide> _activeSide;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Action<FilePanelState, PanelSide> _startWatching;

    public ChangeDirectoryCommandExecutor(
        PanelController controller,
        Func<FilePanelState> activeState,
        Func<PanelSide> activeSide,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Action<FilePanelState, PanelSide> startWatching)
    {
        _controller = controller;
        _activeState = activeState;
        _activeSide = activeSide;
        _panelOptions = panelOptions;
        _startWatching = startWatching;
    }

    public bool TryExecute(string command)
    {
        if (!ChangeDirectoryCommandParser.TryParseTarget(command, out string? rawTarget))
            return false;

        var state = _activeState();
        if (state.SourceId != PanelSourceId.Local)
            return true;

        string targetDirectory;
        try
        {
            string target = Environment.ExpandEnvironmentVariables(rawTarget);
            targetDirectory = Path.GetFullPath(target, state.CurrentDirectory);
            if (!Directory.Exists(targetDirectory))
                return true;
        }
        catch (Exception ex) when (IsChangeDirectoryException(ex))
        {
            return true;
        }

        if (string.Equals(
            Path.GetFullPath(state.CurrentDirectory),
            targetDirectory,
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            if (_controller.TryLoadDirectory(state, targetDirectory, _panelOptions()))
                _startWatching(state, _activeSide());
        }
        catch (Exception ex) when (IsChangeDirectoryException(ex))
        {
        }

        return true;
    }

    private static bool IsChangeDirectoryException(Exception exception) =>
        exception is UnauthorizedAccessException
            or IOException
            or ArgumentException
            or NotSupportedException
            or DirectoryNotFoundException;
}
