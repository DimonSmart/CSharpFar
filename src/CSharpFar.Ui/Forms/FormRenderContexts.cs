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

    public ScreenRenderer Screen => _renderContext.Screen;
    public ConsoleViewport Viewport => _renderContext.Viewport;
    public Rect BodyBounds { get; }
    public CellStyle ScrollbarStyle { get; }
    public Rect? FooterBounds { get; }

    public void PublishOnStable(Action commit) => _renderContext.PublishOnStable(commit);
    public void PublishOnStable<T>(T value, Action<T> commit) => _renderContext.PublishOnStable(value, commit);
}

public sealed class FormRowRenderContext
{
    public FormRowRenderContext(ScreenRenderer screen, Rect bounds, bool focused, int? screenHeight = null)
    {
        Screen = screen;
        Bounds = bounds;
        Focused = focused;
        ScreenHeight = screenHeight ?? screen.FrameViewport.Height;
    }

    public ScreenRenderer Screen { get; }
    public Rect Bounds { get; }
    public bool Focused { get; }
    public int ScreenHeight { get; }
}

public sealed class FormRowInputContext
{
    public FormRowInputContext(
        int rowIndex,
        bool focused,
        int availableDropdownContentRows = 0,
        string? rowId = null,
        FormRowRole rowRole = FormRowRole.Normal,
        Rect? bounds = null,
        int screenHeight = 0)
    {
        RowIndex = rowIndex;
        Focused = focused;
        AvailableDropdownContentRows = availableDropdownContentRows;
        RowId = rowId;
        RowRole = rowRole;
        Bounds = bounds;
        ScreenHeight = screenHeight;
    }

    public int RowIndex { get; }
    public bool Focused { get; }
    public int AvailableDropdownContentRows { get; }
    public string? RowId { get; }
    public FormRowRole RowRole { get; }
    public Rect? Bounds { get; }
    public int ScreenHeight { get; }
}

public sealed class FormRowMouseContext
{
    public FormRowMouseContext(
        Rect bounds,
        int rowIndex,
        bool focused,
        int screenHeight,
        string? rowId = null,
        FormRowRole rowRole = FormRowRole.Normal)
    {
        Bounds = bounds;
        RowIndex = rowIndex;
        Focused = focused;
        ScreenHeight = screenHeight;
        RowId = rowId;
        RowRole = rowRole;
    }

    public Rect Bounds { get; }
    public int RowIndex { get; }
    public bool Focused { get; }
    public int ScreenHeight { get; }
    public string? RowId { get; }
    public FormRowRole RowRole { get; }
}

