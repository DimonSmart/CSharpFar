using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Console;

namespace CSharpFar.App.Viewer;

/// <summary>
/// Full-screen file viewer facade.
/// </summary>
internal sealed class FileViewer
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public FileViewer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public void Show(string filePath)
    {
        if (!File.Exists(filePath))
        {
            new MessageDialog(_screen).Show("Viewer", "File not found.");
            return;
        }

        new LargeFileViewer(_screen, _palette).Show(filePath);
    }

    internal void Show(string filePath, LargeFileViewerOptions options)
    {
        if (!File.Exists(filePath))
        {
            new MessageDialog(_screen).Show("Viewer", "File not found.");
            return;
        }

        new LargeFileViewer(_screen, _palette).Show(filePath, options);
    }
}
