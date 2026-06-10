using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Files;

internal sealed class PanelFileOpener
{
    private readonly IFileLauncher _fileLauncher;
    private readonly ScreenRenderer _screen;
    private readonly Func<ConsolePalette> _palette;
    private readonly Action<FilePanelState, FilePanelItem> _viewPanelFile;
    private readonly Action<string, string, Action> _executeInCurrentConsole;

    public PanelFileOpener(
        IFileLauncher fileLauncher,
        ScreenRenderer screen,
        Func<ConsolePalette> palette,
        Action<FilePanelState, FilePanelItem> viewPanelFile,
        Action<string, string, Action> executeInCurrentConsole)
    {
        _fileLauncher = fileLauncher;
        _screen = screen;
        _palette = palette;
        _viewPanelFile = viewPanelFile;
        _executeInCurrentConsole = executeInCurrentConsole;
    }

    public void OpenFileItem(FilePanelState activeState, FilePanelItem item)
    {
        if (!HasCapability(activeState, PanelProviderCapabilities.OpenRead))
            return;

        try
        {
            if (activeState.SourceId != PanelSourceId.Local)
            {
                _viewPanelFile(activeState, item);
                return;
            }

            string workDir = activeState.CurrentDirectory;
            if (_fileLauncher.GetLaunchMode(item.FullPath) == FileLaunchMode.CurrentConsole)
            {
                _executeInCurrentConsole(
                    workDir,
                    item.FullPath,
                    () => _fileLauncher.OpenFile(item.FullPath, workDir));
                return;
            }

            _fileLauncher.OpenFile(item.FullPath, workDir);
        }
        catch (Exception ex) when (
            ex is IOException or
                  UnauthorizedAccessException or
                  InvalidOperationException or
                  System.ComponentModel.Win32Exception)
        {
            new MessageDialog(_screen, _palette()).Show("Open file", ex.Message);
        }
    }

    private static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;
}
