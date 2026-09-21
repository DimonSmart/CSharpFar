using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

/// <summary>
/// Full-screen file viewer facade.
/// </summary>
internal sealed class FileViewer
{
    private readonly InteractiveSurfaceHost _surfaces;
    private readonly ModalDialogHost _modalDialogs;
    private readonly DialogService _dialogs;
    private readonly CSharpFarPalette _palette;
    private readonly FormFieldFactory _fields;
    private readonly ILocalFileMonitorFactory? _localFileMonitorFactory;
    private readonly ILocalFileByteReaderFactory? _localFileByteReaderFactory;

    public FileViewer(
        InteractiveSurfaceHost surfaces,
        ModalDialogHost modalDialogs,
        DialogService dialogs,
        FormFieldFactory fields,
        CSharpFarPalette? palette = null,
        ILocalFileMonitorFactory? localFileMonitorFactory = null,
        ILocalFileByteReaderFactory? localFileByteReaderFactory = null)
    {
        _surfaces = surfaces;
        _modalDialogs = modalDialogs;
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _palette = palette ?? CSharpFarPaletteRegistry.Default;
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _localFileMonitorFactory = localFileMonitorFactory;
        _localFileByteReaderFactory = localFileByteReaderFactory;
    }

    public void Show(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _dialogs.Message("Viewer", "File not found.");
            return;
        }

        new LargeFileViewer(
            _surfaces,
            _modalDialogs,
            _dialogs,
            _fields,
            _palette,
            _localFileMonitorFactory,
            _localFileByteReaderFactory).Show(filePath);
    }

    internal void Show(string filePath, LargeFileViewerOptions options)
    {
        if (!File.Exists(filePath))
        {
            _dialogs.Message("Viewer", "File not found.");
            return;
        }

        new LargeFileViewer(
            _surfaces,
            _modalDialogs,
            _dialogs,
            _fields,
            _palette,
            _localFileMonitorFactory,
            _localFileByteReaderFactory).Show(filePath, options);
    }

    internal void Show(string displayPath, IFileByteReader reader, LargeFileViewerOptions? options = null) =>
        new LargeFileViewer(
            _surfaces,
            _modalDialogs,
            _dialogs,
            _fields,
            _palette,
            _localFileMonitorFactory,
            _localFileByteReaderFactory).ShowVirtual(displayPath, reader, options);
}
