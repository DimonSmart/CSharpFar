using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui.Tests;

public sealed class TransientPopupTests
{
    [Fact]
    public void TextPrompt_RendersFocusCursorAndUnicodeAwareViewport()
    {
        bool active = true;
        string text = "界abcdefghij";
        var layer = new TransientTextPromptLayer(new TransientTextPromptDefinition
        {
            Id = "test.prompt",
            IsActive = () => active,
            Text = () => text,
            TextChanged = value => text = value,
            Title = "Search",
            Placement = TextPlacement(),
            Appearance = TextAppearance(),
            Cancelled = () => active = false,
        });
        var host = new UiLayerTestHost(layer, width: 20, height: 8);

        host.Render();

        Assert.True(layer.CommittedFrame.Active);
        Assert.True(layer.CommittedFrame.PopupVisible);
        UiFocusEntry focus = Assert.Single(layer.CommittedInteractionFrame.Focus.Entries);
        Assert.Equal(layer.CommittedFrame.Cursor, focus.Cursor);
        Assert.NotNull(focus.Cursor);
        Assert.Equal((focus.Cursor!.X, focus.Cursor.Y), (host.Driver.CursorX, host.Driver.CursorY));
        Assert.True(host.Driver.CursorVisible);
        Assert.Equal(' ', host.Driver.GetCell(focus.Cursor.X, focus.Cursor.Y).Character);
    }

    [Fact]
    public void TextPrompt_OutsideClickDismissesAndBubblesSameClick()
    {
        bool active = true;
        bool cancelled = false;
        var layer = new TransientTextPromptLayer(new TransientTextPromptDefinition
        {
            Id = "test.prompt",
            IsActive = () => active,
            Text = () => "abc",
            TextChanged = _ => { },
            Placement = TextPlacement(),
            Appearance = TextAppearance(),
            Cancelled = () => { cancelled = true; active = false; },
            DismissOnOutsideClick = true,
            BubbleOutsideClick = true,
        });
        var host = new UiLayerTestHost(layer, width: 40, height: 12);
        host.Render();

        UiInputResult result = host.Dispatch(UiTestInput.Mouse(0, 0));

        Assert.True(cancelled);
        Assert.False(result.Handled);
        Assert.True(result.Invalidate);
    }

    [Fact]
    public void TextPrompt_ResizeRecalculatesBoundsAndCursor()
    {
        string text = "abcdefghij";
        var layer = new TransientTextPromptLayer(new TransientTextPromptDefinition
        {
            Id = "test.prompt",
            IsActive = () => true,
            Text = () => text,
            TextChanged = value => text = value,
            Placement = TextPlacement(),
            Appearance = TextAppearance(),
        });
        var host = new UiLayerTestHost(layer, width: 40, height: 12);
        host.Render();
        Rect first = layer.CommittedFrame.PopupBounds;

        host.Resize(24, 9);
        host.Render();

        Assert.NotEqual(first, layer.CommittedFrame.PopupBounds);
        Assert.True(layer.CommittedFrame.PopupVisible);
        Assert.NotNull(layer.CommittedFrame.Cursor);
        Assert.True(layer.CommittedFrame.InputBounds.Contains(
            layer.CommittedFrame.Cursor!.X,
            layer.CommittedFrame.Cursor.Y));
    }

    [Fact]
    public void SelectionPopup_KeyboardAndMouseActivateSemanticItems()
    {
        IReadOnlyList<string> items = ["zero", "one", "two", "three"];
        int selected = 0;
        var activations = new List<TransientSelectionActivation<string>>();
        var layer = SelectionLayer(
            () => items,
            () => selected,
            value => selected = value,
            activation => { activations.Add(activation); return TransientPopupAction.KeepOpen; },
            activateOnMouseDown: true);
        var host = new UiLayerTestHost(layer, width: 30, height: 12);
        host.Render();

        UiInputResult down = host.Dispatch(UiTestInput.Key(ConsoleKey.DownArrow));
        Assert.True(down.Handled);
        Assert.Equal(1, selected);
        host.Render();

        UiInputResult enter = host.Dispatch(UiTestInput.Key(ConsoleKey.Enter));
        Assert.True(enter.Handled);
        Assert.Equal("one", Assert.Single(activations).Item);
        Assert.Equal(TransientSelectionActivationSource.Keyboard, activations[0].Source);

        host.Render();
        Rect row = layer.CommittedFrame.Items.Single(item => item.AbsoluteIndex == 2).Bounds;
        UiInputResult click = host.Dispatch(UiTestInput.Mouse(row.X + 1, row.Y));
        Assert.True(click.Handled);
        Assert.Equal(2, selected);
        Assert.Equal(2, activations.Count);
        Assert.Equal("two", activations[1].Item);
        Assert.Equal(TransientSelectionActivationSource.Pointer, activations[1].Source);
    }

    [Fact]
    public void SelectionPopup_WheelScrollsAndKeepsSelectionVisible()
    {
        IReadOnlyList<string> items = Enumerable.Range(0, 20).Select(i => $"item-{i}").ToArray();
        int selected = 0;
        var layer = SelectionLayer(() => items, () => selected, value => selected = value);
        var host = new UiLayerTestHost(layer, width: 30, height: 10);
        host.Render();
        Rect row = layer.CommittedFrame.Items[0].Bounds;

        for (int i = 0; i < 8; i++)
        {
            UiInputResult result = host.Dispatch(new MouseConsoleInputEvent(
                row.X + 1, row.Y, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None));
            Assert.True(result.Handled);
            host.Render();
        }

        Assert.Equal(8, selected);
        Assert.True(layer.CommittedFrame.List.ScrollTop > 0);
        Assert.InRange(selected,
            layer.CommittedFrame.List.ScrollTop,
            layer.CommittedFrame.List.ScrollTop + layer.CommittedFrame.VisibleRows - 1);
    }

    [Fact]
    public void SelectionPopup_OutsideClickDismissesAndBubbles()
    {
        bool visible = true;
        bool dismissed = false;
        IReadOnlyList<string> items = ["one", "two"];
        int selected = 0;
        var layer = new TransientSelectionPopupLayer<string>(new TransientSelectionPopupDefinition<string>
        {
            Id = "test.selection",
            IsVisible = () => visible,
            Items = () => items,
            ItemText = static item => item,
            SelectedIndex = () => selected,
            SelectionChanged = value => selected = value,
            Placement = SelectionPlacement(),
            Appearance = SelectionAppearance(),
            Dismissed = () => { dismissed = true; visible = false; },
            DismissOnOutsideClick = true,
            BubbleOutsideClick = true,
        });
        var host = new UiLayerTestHost(layer, width: 30, height: 12);
        host.Render();

        UiInputResult result = host.Dispatch(UiTestInput.Mouse(29, 11));

        Assert.True(dismissed);
        Assert.False(result.Handled);
        Assert.True(result.Invalidate);
    }

    private static TransientSelectionPopupLayer<string> SelectionLayer(
        Func<IReadOnlyList<string>> items,
        Func<int> selected,
        Action<int> selectionChanged,
        Func<TransientSelectionActivation<string>, TransientPopupAction>? activated = null,
        bool activateOnMouseDown = false) =>
        new(new TransientSelectionPopupDefinition<string>
        {
            Id = "test.selection",
            IsVisible = () => true,
            Items = items,
            ItemText = static item => item,
            SelectedIndex = selected,
            SelectionChanged = selectionChanged,
            ItemIdentity = static item => item,
            Placement = SelectionPlacement(),
            Appearance = SelectionAppearance(),
            Activated = activated,
            ActivateOnMouseDown = activateOnMouseDown,
            DismissOnOutsideClick = true,
            BubbleOutsideClick = true,
        });

    private static TransientPopupPlacement TextPlacement() => new()
    {
        Anchor = size => new Rect(0, 0, size.Width, size.Height),
        Mode = TransientPopupPlacementMode.CenteredBottomInsideAnchor,
        PreferredWidth = 14,
        MinimumWidth = 8,
        HorizontalInset = 2,
        VerticalInset = 1,
    };

    private static TransientPopupPlacement SelectionPlacement() => new()
    {
        Anchor = size => new Rect(0, size.Height - 1, size.Width, 1),
        Mode = TransientPopupPlacementMode.AboveAnchor,
        PreferredWidth = 20,
        MinimumWidth = 6,
        MaxVisibleRows = 5,
        ReservedRowsAbove = 1,
    };

    private static TransientTextPromptAppearance TextAppearance() =>
        new(Popup(), CellStyle.Default, CellStyle.Default);

    private static TransientSelectionPopupAppearance SelectionAppearance() =>
        new(Popup(), CellStyle.Default, CellStyle.Default, CellStyle.Default);

    private static PopupRenderOptions Popup() => new()
    {
        DrawShadow = false,
        BorderStyle = CellStyle.Default,
        BackgroundStyle = CellStyle.Default,
        ShadowStyle = CellStyle.Default,
    };
}
