using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed class HelpViewer
{
    private readonly InteractiveSurfaceHost _surfaces;
    private readonly ConsolePalette _palette;

    public HelpViewer(InteractiveSurfaceHost surfaces, ConsolePalette? palette = null)
    {
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        _palette = palette ?? PaletteRegistry.Default;
    }

    public void Show()
    {
        var layer = new HelpViewerLayer(HelpContent.Lines, _palette);
        _surfaces.Run(layer, static (_, action) => action == HelpAction.Close
            ? ModalDialogLoopResult<bool>.Complete(true)
            : ModalDialogLoopResult<bool>.Continue);
    }
}

internal enum HelpAction { None, Close }
