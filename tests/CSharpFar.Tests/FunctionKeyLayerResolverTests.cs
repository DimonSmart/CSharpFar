using CSharpFar.App.FunctionKeys;

namespace CSharpFar.Tests;

public sealed class FunctionKeyLayerResolverTests
{
    [Theory]
    [InlineData(0, (int)FunctionKeyLayer.Plain)]
    [InlineData((int)ConsoleModifiers.Alt, (int)FunctionKeyLayer.Alt)]
    [InlineData((int)ConsoleModifiers.Control, (int)FunctionKeyLayer.Control)]
    [InlineData((int)ConsoleModifiers.Shift, (int)FunctionKeyLayer.Shift)]
    [InlineData((int)(ConsoleModifiers.Alt | ConsoleModifiers.Control), (int)FunctionKeyLayer.Plain)]
    [InlineData((int)(ConsoleModifiers.Alt | ConsoleModifiers.Shift), (int)FunctionKeyLayer.Plain)]
    [InlineData((int)(ConsoleModifiers.Control | ConsoleModifiers.Shift), (int)FunctionKeyLayer.Plain)]
    [InlineData((int)(ConsoleModifiers.Alt | ConsoleModifiers.Control | ConsoleModifiers.Shift), (int)FunctionKeyLayer.Plain)]
    public void ResolvePressedLayerMapsSupportedModifierStates(int modifiers, int expectedLayer)
    {
        Assert.Equal(
            (FunctionKeyLayer)expectedLayer,
            FunctionKeyLayerResolver.ResolvePressedLayer((ConsoleModifiers)modifiers));
    }

    [Theory]
    [InlineData(0, (int)FunctionKeyLayer.Plain)]
    [InlineData((int)ConsoleModifiers.Alt, (int)FunctionKeyLayer.Alt)]
    [InlineData((int)ConsoleModifiers.Control, (int)FunctionKeyLayer.Control)]
    [InlineData((int)ConsoleModifiers.Shift, (int)FunctionKeyLayer.Shift)]
    public void TryResolveChordLayerResolvesSingleModifierLayers(int modifiers, int expectedLayer)
    {
        bool result = FunctionKeyLayerResolver.TryResolveChordLayer((ConsoleModifiers)modifiers, out var layer);

        Assert.True(result);
        Assert.Equal((FunctionKeyLayer)expectedLayer, layer);
    }

    [Theory]
    [InlineData((int)(ConsoleModifiers.Alt | ConsoleModifiers.Control))]
    [InlineData((int)(ConsoleModifiers.Alt | ConsoleModifiers.Shift))]
    [InlineData((int)(ConsoleModifiers.Control | ConsoleModifiers.Shift))]
    [InlineData((int)(ConsoleModifiers.Alt | ConsoleModifiers.Control | ConsoleModifiers.Shift))]
    public void TryResolveChordLayerRejectsCombinedModifierLayers(int modifiers)
    {
        bool result = FunctionKeyLayerResolver.TryResolveChordLayer((ConsoleModifiers)modifiers, out _);

        Assert.False(result);
    }
}
