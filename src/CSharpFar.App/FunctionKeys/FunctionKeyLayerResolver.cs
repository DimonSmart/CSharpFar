namespace CSharpFar.App.FunctionKeys;

internal static class FunctionKeyLayerResolver
{
    public static FunctionKeyLayer ResolvePressedLayer(ConsoleModifiers modifiers)
    {
        bool hasAlt = (modifiers & ConsoleModifiers.Alt) != 0;
        bool hasControl = (modifiers & ConsoleModifiers.Control) != 0;
        bool hasShift = (modifiers & ConsoleModifiers.Shift) != 0;

        if (hasAlt && !hasControl && !hasShift)
            return FunctionKeyLayer.Alt;

        if (hasControl && !hasAlt && !hasShift)
            return FunctionKeyLayer.Control;

        if (hasShift && !hasAlt && !hasControl)
            return FunctionKeyLayer.Shift;

        if (!hasAlt && !hasControl && !hasShift)
            return FunctionKeyLayer.Plain;

        return FunctionKeyLayer.Plain;
    }

    public static bool TryResolveChordLayer(ConsoleModifiers modifiers, out FunctionKeyLayer layer)
    {
        bool hasAlt = (modifiers & ConsoleModifiers.Alt) != 0;
        bool hasControl = (modifiers & ConsoleModifiers.Control) != 0;
        bool hasShift = (modifiers & ConsoleModifiers.Shift) != 0;

        if (!hasAlt && !hasControl && !hasShift)
        {
            layer = FunctionKeyLayer.Plain;
            return true;
        }

        if (hasAlt && !hasControl && !hasShift)
        {
            layer = FunctionKeyLayer.Alt;
            return true;
        }

        if (hasControl && !hasAlt && !hasShift)
        {
            layer = FunctionKeyLayer.Control;
            return true;
        }

        if (hasShift && !hasAlt && !hasControl)
        {
            layer = FunctionKeyLayer.Shift;
            return true;
        }

        layer = FunctionKeyLayer.Plain;
        return false;
    }
}
