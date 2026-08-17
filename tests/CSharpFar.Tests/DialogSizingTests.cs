using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class DialogSizingTests
{
    [Fact]
    public void Resolve_DefaultModeKeepsPreferredSize()
    {
        (int width, int height) = DialogSizing.Resolve(new ConsoleSize(140, 40), 80, 20);

        Assert.Equal((80, 20), (width, height));
    }

    [Fact]
    public void Resolve_WidthModeExpandsOnlyWidth()
    {
        (int width, int height) = DialogSizing.Resolve(new ConsoleSize(140, 40), 80, 20, DialogResizeMode.Width);

        Assert.Equal((136, 20), (width, height));
    }

    [Fact]
    public void Resolve_HeightModeExpandsOnlyHeight()
    {
        (int width, int height) = DialogSizing.Resolve(new ConsoleSize(140, 40), 80, 20, DialogResizeMode.Height);

        Assert.Equal((80, 38), (width, height));
    }

    [Fact]
    public void Resolve_BothModeExpandsBothDimensions()
    {
        (int width, int height) = DialogSizing.Resolve(new ConsoleSize(140, 40), 80, 20, DialogResizeMode.Both);

        Assert.Equal((136, 38), (width, height));
    }

    [Fact]
    public void Resolve_SmallViewportPreservesPreferredSizeForViewportConstraint()
    {
        (int width, int height) = DialogSizing.Resolve(new ConsoleSize(70, 15), 92, 24, DialogResizeMode.Both);

        Assert.Equal((92, 24), (width, height));
        ModalDialogRenderer.Layout layout = new ModalDialogRenderer().CalculateLayout(new ConsoleSize(70, 15), width, height);
        Assert.Equal((70, 15), (layout.OuterBounds.Width, layout.OuterBounds.Height));
    }
}
