using CSharpFar.App.Editor;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Files;

internal sealed class PanelFileViewerService
{
    private readonly InteractiveSurfaceHost _surfaces;
    private readonly ModalDialogHost _modalDialogs;
    private readonly Func<ConsolePalette> _palette;
    private readonly FilePanelSourceRegistry _sourceRegistry;
    private readonly IHistoryStore _history;
    private readonly ITextClipboard _clipboard;
    private readonly AppSettingsAlias _settings;
    private readonly PanelController _controller;
    private readonly Func<FilePanelState, PanelSide> _panelSideForState;
    private readonly Func<PanelSide, int> _visibleRows;
    private readonly Action<FilePanelState, int> _safeRefresh;

    public PanelFileViewerService(
        InteractiveSurfaceHost surfaces,
        ModalDialogHost modalDialogs,
        Func<ConsolePalette> palette,
        FilePanelSourceRegistry sourceRegistry,
        IHistoryStore history,
        ITextClipboard clipboard,
        AppSettingsAlias settings,
        PanelController controller,
        Func<FilePanelState, PanelSide> panelSideForState,
        Func<PanelSide, int> visibleRows,
        Action<FilePanelState, int> safeRefresh)
    {
        _surfaces = surfaces;
        _modalDialogs = modalDialogs;
        _palette = palette;
        _sourceRegistry = sourceRegistry;
        _history = history;
        _clipboard = clipboard;
        _settings = settings;
        _controller = controller;
        _panelSideForState = panelSideForState;
        _visibleRows = visibleRows;
        _safeRefresh = safeRefresh;
    }

    public void ViewPanelFile(FilePanelState state, FilePanelItem item)
    {
        if (state.SourceId == PanelSourceId.Local)
        {
            _history.AddFile(new FileHistoryItem { Path = item.FullPath });
            new FileViewer(_surfaces, _modalDialogs, _palette()).Show(item.FullPath, BuildLocalViewerOptions(state, item));
            _safeRefresh(state, _visibleRows(_panelSideForState(state)));
            return;
        }

        var source = _sourceRegistry.GetSource(item.SourceId);
        string tempPath = Path.Combine(Path.GetTempPath(), "CSharpFar", Guid.NewGuid().ToString("N"), item.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        try
        {
            using (var input = source.OpenReadAsync(item.SourcePath).GetAwaiter().GetResult())
            using (var output = File.Create(tempPath))
            {
                input.CopyTo(output);
            }

            _history.AddFile(new FileHistoryItem { Path = $"{item.SourceId}:{item.SourcePath}" });
            new FileViewer(_surfaces, _modalDialogs, _palette()).Show(tempPath);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(tempPath)!, recursive: true); }
            catch { }
        }
    }

    private LargeFileViewerOptions BuildLocalViewerOptions(FilePanelState state, FilePanelItem item)
    {
        var filePaths = state.Items
            .Where(panelItem => !panelItem.IsParentDirectory && !panelItem.IsDirectory)
            .Select(panelItem => panelItem.FullPath)
            .ToArray();
        int currentIndex = Array.FindIndex(
            filePaths,
            path => string.Equals(path, item.FullPath, StringComparison.OrdinalIgnoreCase));

        return new LargeFileViewerOptions
        {
            FilePaths = filePaths,
            CurrentFileIndex = currentIndex,
            Clipboard = _clipboard,
            EditFile = path =>
            {
                _history.AddFile(new FileHistoryItem { Path = path });
                new FileEditor(_surfaces, _modalDialogs, _palette(), _settings.Editor, _clipboard).Show(path);
            },
            CurrentFileChanged = path =>
            {
                int panelIndex = state.Items.FindIndex(
                    panelItem => string.Equals(panelItem.FullPath, path, StringComparison.OrdinalIgnoreCase));
                if (panelIndex >= 0)
                    _controller.SetCursorTo(state, panelIndex, _visibleRows(_panelSideForState(state)));
            },
        };
    }
}
