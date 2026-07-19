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
    public void ResolvePressedLayerMapsSupportedModifierStates(int modifiers, int expectedLayer)
    {
        Assert.Equal(
            (FunctionKeyLayer)expectedLayer,
            FunctionKeyLayerResolver.ResolvePressedLayer((ConsoleModifiers)modifiers));
    }

    [Theory]
    [InlineData(0, true, (int)FunctionKeyLayer.Plain)]
    [InlineData((int)ConsoleModifiers.Alt, true, (int)FunctionKeyLayer.Alt)]
    [InlineData((int)ConsoleModifiers.Control, true, (int)FunctionKeyLayer.Control)]
    [InlineData((int)ConsoleModifiers.Shift, true, (int)FunctionKeyLayer.Shift)]
    [InlineData((int)(ConsoleModifiers.Alt | ConsoleModifiers.Control), false, (int)FunctionKeyLayer.Plain)]
    public void TryResolveChordLayerRejectsCombinedModifierLayers(
        int modifiers,
        bool expectedResult,
        int expectedLayer)
    {
        Assert.Equal(
            expectedResult,
            FunctionKeyLayerResolver.TryResolveChordLayer((ConsoleModifiers)modifiers, out var layer));
        Assert.Equal((FunctionKeyLayer)expectedLayer, layer);
    }
}
