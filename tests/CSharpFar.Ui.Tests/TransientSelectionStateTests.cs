using CSharpFar.Console.Models;

namespace CSharpFar.Ui.Tests;

public sealed class TransientSelectionStateTests
{
    [Fact]
    public void ItemRefreshPreservesSelectionByIdentity()
    {
        IReadOnlyList<string> items = ["zero", "one", "two"];
        int selected = 2;
        var layer = new TransientSelectionPopupLayer<string>(new TransientSelectionPopupDefinition<string>
        {
            Id = "test.selection-preservation",
            IsVisible = () => true,
            Items = () => items,
            ItemText = static item => item,
            SelectedIndex = () => selected,
            SelectionChanged = value => selected = value,
            ItemIdentity = static item => item,
            Placement = new TransientPopupPlacement
            {
                Anchor = size => new Rect(0, size.Height - 1, size.Width, 1),
                Mode = TransientPopupPlacementMode.AboveAnchor,
                PreferredWidth = 20,
                MinimumWidth = 6,
                MaxVisibleRows = 5,
                ReservedRowsAbove = 1,
            },
            Appearance = new TransientSelectionPopupAppearance(
                Popup(), CellStyle.Default, CellStyle.Default, CellStyle.Default),
        });
        var host = new UiLayerTestHost(layer, width: 30, height: 12);
        host.Render();
        Assert.Equal(2, selected);

        items = ["two", "zero", "one"];
        host.Render();

        Assert.Equal(0, selected);
        Assert.Equal("two", layer.CommittedFrame.Items.Single(item => item.AbsoluteIndex == selected).Item);
    }

    private static PopupRenderOptions Popup() => new()
    {
        DrawShadow = false,
        BorderStyle = CellStyle.Default,
        BackgroundStyle = CellStyle.Default,
        ShadowStyle = CellStyle.Default,
    };
}
