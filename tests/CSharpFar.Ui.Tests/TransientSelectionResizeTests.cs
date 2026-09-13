using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui.Tests;

public sealed class TransientSelectionResizeTests
{
    [Fact]
    public void CommittedResizeRebasesActiveScrollbarDrag()
    {
        IReadOnlyList<string> items = Enumerable.Range(0, 20).Select(i => $"item-{i}").ToArray();
        int selected = 0;
        var layer = new TransientSelectionPopupLayer<string>(new TransientSelectionPopupDefinition<string>
        {
            Id = "test.resize-selection",
            IsVisible = () => true,
            Items = () => items,
            ItemText = static item => item,
            SelectedIndex = () => selected,
            SelectionChanged = value => selected = value,
            Placement = new TransientPopupPlacement
            {
                Anchor = size => new Rect(0, size.Height - 1, size.Width, 1),
                Mode = TransientPopupPlacementMode.AboveAnchor,
                PreferredWidth = 0,
                MinimumWidth = 1,
                MaxVisibleRows = 8,
                ReservedRowsAbove = 2,
            },
            Appearance = new TransientSelectionPopupAppearance(
                Popup(), CellStyle.Default, CellStyle.Default, CellStyle.Default),
        });
        var host = new UiLayerTestHost(layer, width: 80, height: 25);
        host.Render();
        Rect scrollbar = Assert.IsType<Rect>(layer.CommittedFrame.ScrollbarBounds);

        UiInputResult down = host.Dispatch(UiTestInput.Mouse(scrollbar.X, scrollbar.Y + 1));
        Assert.True(down.Handled);

        host.Resize(100, 35);
        host.Render();
        Rect resized = Assert.IsType<Rect>(layer.CommittedFrame.ScrollbarBounds);

        UiInputResult move = host.Dispatch(new MouseConsoleInputEvent(
            resized.X,
            Math.Min(resized.Bottom - 1, resized.Y + 5),
            MouseButton.Left,
            MouseEventKind.Move,
            MouseKeyModifiers.None));
        Assert.True(move.Handled);
        host.Render();

        Assert.True(layer.CommittedFrame.List.ScrollTop > 0);
    }

    private static PopupRenderOptions Popup() => new()
    {
        DrawShadow = false,
        BorderStyle = CellStyle.Default,
        BackgroundStyle = CellStyle.Default,
        ShadowStyle = CellStyle.Default,
    };
}
