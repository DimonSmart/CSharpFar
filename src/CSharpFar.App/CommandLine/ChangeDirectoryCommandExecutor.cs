using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.CommandLine;

internal sealed class ChangeDirectoryCommandExecutor
{
    private readonly PanelController _controller;
    private readonly Func<FilePanelState> _activeState;
    private readonly Func<PanelSide> _activeSide;
    private readonly Func<bool> _isLocalSourceAvailable;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Action<FilePanelState, PanelSide> _startWatching;
    private readonly ILocalPathNormalizer _localPathNormalizer;

    public ChangeDirectoryCommandExecutor(
        PanelController controller,
        Func<FilePanelState> activeState,
        Func<PanelSide> activeSide,
        Func<bool> isLocalSourceAvailable,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Action<FilePanelState, PanelSide> startWatching,
        ILocalPathNormalizer? localPathNormalizer = null)
    {
        _controller = controller;
        _activeState = activeState;
        _activeSide = activeSide;
        _isLocalSourceAvailable = isLocalSourceAvailable;
        _panelOptions = panelOptions;
        _startWatching = startWatching;
        _localPathNormalizer = localPathNormalizer ?? LocalPathNormalizer.Current;
    }

    public bool TryExecute(string command)
    {
        if (!ChangeDirectoryCommandParser.TryParseTarget(command, out string? rawTarget))
            return false;

        var state = _activeState();
        if (state.SourceId != PanelSourceId.Local || !_isLocalSourceAvailable())
            return true;

        string targetDirectory;
        try
        {
            string target = Environment.ExpandEnvironmentVariables(rawTarget);
            if (!_localPathNormalizer.TryNormalize(
                target,
                state.CurrentDirectory,
                out targetDirectory))
            {
                return true;
            }

            if (!Directory.Exists(targetDirectory))
                return true;
        }
        catch (Exception ex) when (IsChangeDirectoryException(ex))
        {
            return true;
        }

        if (string.Equals(
            state.CurrentDirectory,
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
