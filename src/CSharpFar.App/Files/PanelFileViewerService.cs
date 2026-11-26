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
    private readonly DialogService _dialogs;
    private readonly Func<CSharpFarPalette> _palette;
    private readonly FilePanelSourceRegistry _sourceRegistry;
    private readonly IHistoryStore _history;
    private readonly ITextClipboard _clipboard;
    private readonly FormFieldFactory _fields;
    private readonly AppSettingsAlias _settings;
    private readonly PanelController _controller;
    private readonly Func<FilePanelState, PanelSide> _panelSideForState;
    private readonly Func<PanelSide, int> _visibleRows;
    private readonly Action<FilePanelState, int> _safeRefresh;
    private readonly IFileLauncher? _fileLauncher;
    private readonly IUriLauncher? _uriLauncher;

    public PanelFileViewerService(
        InteractiveSurfaceHost surfaces,
        ModalDialogHost modalDialogs,
        DialogService dialogs,
        Func<CSharpFarPalette> palette,
        FilePanelSourceRegistry sourceRegistry,
        IHistoryStore history,
        ITextClipboard clipboard,
        FormFieldFactory fields,
        AppSettingsAlias settings,
        PanelController controller,
        Func<FilePanelState, PanelSide> panelSideForState,
        Func<PanelSide, int> visibleRows,
        Action<FilePanelState, int> safeRefresh,
        IFileLauncher? fileLauncher = null,
        IUriLauncher? uriLauncher = null)
    {
        _surfaces = surfaces;
        _modalDialogs = modalDialogs;
        _dialogs = dialogs;
        _palette = palette;
        _sourceRegistry = sourceRegistry;
        _history = history;
        _clipboard = clipboard;
        _fields = fields;
        _settings = settings;
        _controller = controller;
        _panelSideForState = panelSideForState;
        _visibleRows = visibleRows;
        _safeRefresh = safeRefresh;
        _fileLauncher = fileLauncher;
        _uriLauncher = uriLauncher;
    }

    public void ViewFile(string path) =>
        new FileViewer(_surfaces, _modalDialogs, _dialogs, _fields, _palette()).Show(path);

    public void EditFile(
        string path,
        EditorFileNameInsertionContext? fileNameInsertionContext = null)
    {
        new FileEditor(
            _surfaces,
            _modalDialogs,
            _dialogs,
            _palette(),
            _settings.Editor,
            _clipboard,
            _fields,
            fileNameInsertionContext,
            null).Show(path);
    }

    public void EditFile(
        PanelLocation location,
        EditorFileNameInsertionContext? fileNameInsertionContext = null)
    {
        if (location.SourceId == PanelSourceId.Local)
        {
            EditFile(location.SourcePath, fileNameInsertionContext);
            return;
        }

        new FileEditor(
            _surfaces,
            _modalDialogs,
            _dialogs,
            _palette(),
            _settings.Editor,
            _clipboard,
            _fields,
            fileNameInsertionContext,
            null,
            _sourceRegistry).Show(location);
    }

    public EditorOpenResult EditFileWithNewFileFormat(
        string path,
        EditorDocumentFormat newFileFormat,
        EditorFileNameInsertionContext? fileNameInsertionContext = null)
    {
        return new FileEditor(
            _surfaces,
            _modalDialogs,
            _dialogs,
            _palette(),
            _settings.Editor,
            _clipboard,
            _fields,
            fileNameInsertionContext,
            null).ShowWithNewFileFormat(path, newFileFormat);
    }

    public EditorOpenResult EditFileWithNewFileFormat(
        PanelLocation location,
        EditorDocumentFormat newFileFormat,
        EditorFileNameInsertionContext? fileNameInsertionContext = null)
    {
        if (location.SourceId == PanelSourceId.Local)
        {
            return EditFileWithNewFileFormat(location.SourcePath, newFileFormat, fileNameInsertionContext);
        }

        return new FileEditor(
            _surfaces,
            _modalDialogs,
            _dialogs,
            _palette(),
            _settings.Editor,
            _clipboard,
            _fields,
            fileNameInsertionContext,
            null,
            _sourceRegistry).ShowWithNewFileFormat(location, newFileFormat);
    }

    public void ViewPanelFile(FilePanelState state, FilePanelItem item)
    {
        if (state.SourceId == PanelSourceId.Local)
        {
            _history.AddFile(new FileHistoryItem { Path = BuildHistoryPath(item) });
            new FileViewer(_surfaces, _modalDialogs, _dialogs, _fields, _palette()).Show(item.FullPath, BuildLocalViewerOptions(state, item));
            _safeRefresh(state, _visibleRows(_panelSideForState(state)));
            return;
        }

        RemoteViewerInput? opened;
        try
        {
            opened = OpenRemoteViewerInput(item);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _dialogs.Message("Viewer", ex.Message);
            return;
        }

        if (opened is null)
            return;

        using (opened)
        {
            _history.AddFile(new FileHistoryItem { Path = BuildHistoryPath(item) });
            new FileViewer(_surfaces, _modalDialogs, _dialogs, _fields, _palette()).Show(
                item.SourcePath, opened.Reader, new LargeFileViewerOptions
                {
                    Clipboard = _clipboard,
                    FileLauncher = _fileLauncher,
                    UriLauncher = _uriLauncher,
                    EditCurrentFile = () => EditPanelFile(state, item),
                });
        }
    }

    private RemoteViewerInput? OpenRemoteViewerInput(FilePanelItem item)
    {
        // Use the existing cancellable operation dialog to keep input responsive
        // during remote stream opening and non-seekable disk spooling.
        var gate = new object();
        bool abandoned = false;
        RemoteViewerInput? prepared = null;
        return _dialogs.Operation<string, RemoteViewerInput?, RemoteViewerInput?>(
            new OperationDialogDefinition<string, RemoteViewerInput?, RemoteViewerInput?>
            {
                Title = "Opening viewer",
                PreferredWidth = 54,
                PreferredHeight = 8,
                MinWidth = 30,
                MinHeight = 8,
                Operation = async cancellationToken =>
                {
                    RemoteViewerInput opened = await CreateRemoteViewerInputAsync(item, cancellationToken)
                        .ConfigureAwait(false);
                    lock (gate)
                    {
                        if (abandoned)
                        {
                            opened.Dispose();
                            return null;
                        }
                        prepared = opened;
                        return opened;
                    }
                },
                Synchronize = () => new OperationDialogState<string>(
                    status: $"Opening {item.Name}... (Esc to cancel)"),
                HandleCancel = () =>
                {
                    lock (gate)
                    {
                        abandoned = true;
                        prepared?.Dispose();
                        prepared = null;
                    }
                    return OperationDialogOutcome<RemoteViewerInput?>.Complete(null);
                },
                HandleOperationCompleted = opened =>
                {
                    lock (gate)
                        prepared = null; // Ownership passes to the viewer.
                    return OperationDialogOutcome<RemoteViewerInput?>.Complete(opened);
                },
                Complete = opened => opened,
            });
    }

    private async Task<RemoteViewerInput> CreateRemoteViewerInputAsync(
        FilePanelItem item,
        CancellationToken cancellationToken)
    {
        IDisposable usage = _sourceRegistry.AcquireUsage(item.SourceId);
        Stream? input = null;
        string? spool = null;
        try
        {
            var source = _sourceRegistry.GetSource(item.SourceId);
            input = await source.AsAsync().OpenReadAsync(item.SourcePath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (input.CanSeek)
                return new RemoteViewerInput(new SeekableStreamFileByteReader(input, usage));

            // Non-seekable streams are buffered on disk, never into an unbounded byte array.
            spool = Path.Combine(Path.GetTempPath(), "csharpfar-view-" + Guid.NewGuid().ToString("N"));
            await using (var output = new FileStream(
                spool, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                bufferSize: 81920, options: FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            await input.DisposeAsync().ConfigureAwait(false);
            input = null;
            usage.Dispose();
            return new RemoteViewerInput(new RandomAccessFileByteReader(spool), spool);
        }
        catch
        {
            try
            {
                if (input is not null) await input.DisposeAsync().ConfigureAwait(false);
            }
            finally { usage.Dispose(); }
            if (spool is not null)
            {
                try { File.Delete(spool); }
                catch (FileNotFoundException) { }
            }
            throw;
        }
    }

    private sealed class RemoteViewerInput(IFileByteReader reader, string? spool = null) : IDisposable
    {
        public IFileByteReader Reader { get; } = reader;

        public void Dispose()
        {
            try
            {
                if (Reader is IDisposable disposable) disposable.Dispose();
            }
            finally
            {
                if (spool is not null)
                {
                    try { File.Delete(spool); }
                    catch (FileNotFoundException) { }
                }
            }
        }
    }

    public void EditPanelFile(FilePanelState state, FilePanelItem item)
    {
        _history.AddFile(new FileHistoryItem { Path = BuildHistoryPath(item) });

        if (state.SourceId == PanelSourceId.Local)
        {
            new FileEditor(_surfaces, _modalDialogs, _dialogs, _palette(), _settings.Editor, _clipboard, _fields, null, null).Show(item.FullPath);
        }
        else
        {
            new FileEditor(_surfaces, _modalDialogs, _dialogs, _palette(), _settings.Editor, _clipboard, _fields, null, null, _sourceRegistry)
                .Show(item.Location);
        }
    }

    internal static string BuildHistoryPath(FilePanelItem item) =>
        item.SourceId == PanelSourceId.Local
            ? item.FullPath
            : $"{item.SourceId}:{item.SourcePath}";

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
            FileLauncher = _fileLauncher,
            UriLauncher = _uriLauncher,
            EditFile = path =>
            {
                _history.AddFile(new FileHistoryItem { Path = path });
                new FileEditor(_surfaces, _modalDialogs, _dialogs, _palette(), _settings.Editor, _clipboard, _fields, null, null).Show(path);
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
