namespace CSharpFar.Console.Input;

internal sealed class MouseInputNormalizer
{
    private readonly Func<long> _getTimestampMilliseconds;
    private readonly long _doubleClickIntervalMilliseconds;
    private MouseClick? _lastDown;

    public MouseInputNormalizer(
        Func<long>? getTimestampMilliseconds = null,
        long doubleClickIntervalMilliseconds = 500)
    {
        _getTimestampMilliseconds = getTimestampMilliseconds ?? (() => Environment.TickCount64);
        _doubleClickIntervalMilliseconds = doubleClickIntervalMilliseconds;
    }

    public MouseConsoleInputEvent Normalize(MouseConsoleInputEvent input)
    {
        if (input.Kind is MouseEventKind.Move or MouseEventKind.Wheel)
        {
            _lastDown = null;
            return input;
        }

        if (input.Kind != MouseEventKind.Down)
            return input;

        long timestamp = _getTimestampMilliseconds();
        MouseClick? previous = _lastDown;
        long elapsed = previous is null
            ? -1
            : timestamp - previous.TimestampMilliseconds;
        if (previous is not null &&
            previous.Button == input.Button &&
            previous.X == input.X &&
            previous.Y == input.Y &&
            previous.Modifiers == input.Modifiers &&
            elapsed is >= 0 &&
            elapsed <= _doubleClickIntervalMilliseconds)
        {
            _lastDown = null;
            return input with { Kind = MouseEventKind.DoubleClick };
        }

        _lastDown = new MouseClick(
            input.Button,
            input.X,
            input.Y,
            input.Modifiers,
            timestamp);
        return input;
    }

    public void Reset() => _lastDown = null;

    private sealed record MouseClick(
        MouseButton Button,
        int X,
        int Y,
        MouseKeyModifiers Modifiers,
        long TimestampMilliseconds);
}
