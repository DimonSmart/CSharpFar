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
    private readonly ConsolePalette _palette;

    public FileViewer(InteractiveSurfaceHost surfaces, ModalDialogHost modalDialogs, ConsolePalette? palette = null)
    {
        _surfaces = surfaces;
        _modalDialogs = modalDialogs;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public void Show(string filePath)
    {
        if (!File.Exists(filePath))
        {
            new MessageDialog(_modalDialogs).Show("Viewer", "File not found.");
            return;
        }

        new LargeFileViewer(_surfaces, _modalDialogs, _palette).Show(filePath);
    }

    internal void Show(string filePath, LargeFileViewerOptions options)
    {
        if (!File.Exists(filePath))
        {
            new MessageDialog(_modalDialogs).Show("Viewer", "File not found.");
            return;
        }

        new LargeFileViewer(_surfaces, _modalDialogs, _palette).Show(filePath, options);
    }
}
