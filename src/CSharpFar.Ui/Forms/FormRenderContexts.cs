using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class FormRenderContext
{
    private readonly UiRenderContext _renderContext;

    public FormRenderContext(
        UiRenderContext renderContext,
        Rect bodyBounds,
        CellStyle? scrollbarStyle = null,
        Rect? footerBounds = null)
    {
        ArgumentNullException.ThrowIfNull(renderContext);

        _renderContext = renderContext;
        BodyBounds = bodyBounds;
        ScrollbarStyle = scrollbarStyle ?? FarDialogStyles.Border;
        FooterBounds = footerBounds;
    }

    public IUiCanvas Canvas => _renderContext.Canvas;
    public ConsoleViewport Viewport => _renderContext.Viewport;
    public Rect BodyBounds { get; }
    public CellStyle ScrollbarStyle { get; }
    public Rect? FooterBounds { get; }

    public void PublishOnStable(Action commit) => _renderContext.PublishOnStable(commit);
    public void PublishOnStable<T>(T value, Action<T> commit) => _renderContext.PublishOnStable(value, commit);
}

public sealed class FormRowRenderContext
{
    public FormRowRenderContext(IUiCanvas screen, Rect bounds, bool focused, FormRowLayout? layout = null)
    {
        Canvas = screen;
        Bounds = bounds;
        Focused = focused;
        Layout = layout ?? new FormRowLayout(bounds, null, bounds);
    }

    public IUiCanvas Canvas { get; }
    public Rect Bounds { get; }
    public bool Focused { get; }
    public FormRowLayout Layout { get; }
}

public readonly record struct FormRowInputContext(bool Focused);

public readonly record struct FormRowMouseContext(bool Focused, FormRowLayout Layout)
{
    public Rect Bounds => Layout.RowBounds;
}

