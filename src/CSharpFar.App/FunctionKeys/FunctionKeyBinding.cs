namespace CSharpFar.App.FunctionKeys;

internal sealed record FunctionKeyBinding(
    string CommandId,
    FunctionKeyLayer Layer,
    ConsoleKey Key,
    string Label,
    bool RunsWhenUnavailable = false)
{
    public int KeyNumber
    {
        get
        {
            if (Key is < ConsoleKey.F1 or > ConsoleKey.F12)
                throw new InvalidOperationException($"Unsupported function key: {Key}");

            return (int)Key - (int)ConsoleKey.F1 + 1;
        }
    }
}
