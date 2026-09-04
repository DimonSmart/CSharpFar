using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed class HelpViewer
{
    private readonly InteractiveSurfaceHost _surfaces;
    private readonly CSharpFarPalette _palette;

    public HelpViewer(InteractiveSurfaceHost surfaces, CSharpFarPalette? palette = null)
    {
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        _palette = palette ?? CSharpFarPaletteRegistry.Default;
    }

    public void Show(HelpTopic topic = HelpTopic.Main)
    {
        HelpPage page = HelpContent.GetPage(topic);
        var layer = new HelpViewerLayer(page.Lines.ToArray(), _palette);
        _surfaces.Run(layer, static (_, action) => action == HelpAction.Close
            ? ModalDialogLoopResult<bool>.Complete(true)
            : ModalDialogLoopResult<bool>.ContinueNoChange);
    }
}

internal enum HelpTopic { Main, Copy }

internal enum HelpAction { None, Close }
