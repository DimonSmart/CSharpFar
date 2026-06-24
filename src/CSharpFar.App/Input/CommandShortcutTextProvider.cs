using CSharpFar.App.FunctionKeys;

namespace CSharpFar.App.Input;

internal sealed class CommandShortcutTextProvider : ICommandShortcutTextProvider
{
    private readonly IReadOnlyList<KeyboardShortcutBinding> _keyboardBindings;
    private readonly IReadOnlyList<FunctionKeyBinding> _functionKeyBindings;

    public CommandShortcutTextProvider(
        IReadOnlyList<KeyboardShortcutBinding> keyboardBindings,
        IReadOnlyList<FunctionKeyBinding> functionKeyBindings)
    {
        _keyboardBindings = keyboardBindings;
        _functionKeyBindings = functionKeyBindings;
    }

    public string? GetPrimaryShortcutText(string commandId)
    {
        var keyboardBinding = _keyboardBindings.FirstOrDefault(binding =>
            string.Equals(binding.CommandId, commandId, StringComparison.Ordinal));
        if (keyboardBinding is not null)
            return keyboardBinding.DisplayText;

        var functionKeyBinding = _functionKeyBindings.FirstOrDefault(binding =>
            string.Equals(binding.CommandId, commandId, StringComparison.Ordinal));
        return functionKeyBinding is null ? null : FormatFunctionKey(functionKeyBinding);
    }

    private static string FormatFunctionKey(FunctionKeyBinding binding)
    {
        string key = $"F{binding.KeyNumber}";
        return binding.Layer switch
        {
            FunctionKeyLayer.Plain => key,
            FunctionKeyLayer.Alt => "Alt+" + key,
            FunctionKeyLayer.Control => "Ctrl+" + key,
            FunctionKeyLayer.Shift => "Shift+" + key,
            _ => key,
        };
    }
}
