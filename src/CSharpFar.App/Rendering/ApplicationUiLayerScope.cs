using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationUiLayerScope : IDisposable
{
    private readonly IDisposable[] _registrations;
    private bool _disposed;

    public ApplicationUiLayerScope(
        UiCompositionHost composition,
        CommandCompletionLayer commandCompletion,
        PanelQuickSearchLayer panelQuickSearch,
        TopMenuLayer topMenu)
    {
        _registrations =
        [
            composition.RegisterOverlay(commandCompletion),
            composition.RegisterOverlay(panelQuickSearch),
            composition.RegisterOverlay(topMenu),
        ];
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        for (int i = _registrations.Length - 1; i >= 0; i--)
            _registrations[i].Dispose();

        _disposed = true;
    }
}
